using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using app;
using domain;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace infra;

public sealed class ApnsService : IApnsService
{
    // Apple's hosts are fixed, so they are constants rather than configuration where they
    // could be mistyped. Which one a push uses is a property of the token, not of the
    // deployment: one server answers Xcode builds and TestFlight builds at the same time.
    private const string ProductionHost = "https://api.push.apple.com";
    private const string SandboxHost = "https://api.sandbox.push.apple.com";

    // A social notification that surfaces hours late is noise, but 0 (try once, then drop)
    // loses every push sent while a phone is in a tunnel. Tune from real delivery data.
    private static readonly TimeSpan Expiration = TimeSpan.FromHours(1);

    private readonly HttpClient _httpClient;
    private readonly IMongoDbContext _dbContext;
    private readonly ApnsTokenProvider _tokenProvider;
    private readonly ILogger<ApnsService> _logger;

    public ApnsService(
        HttpClient httpClient,
        IMongoDbContext dbContext,
        ApnsTokenProvider tokenProvider,
        ILogger<ApnsService> logger)
    {
        _httpClient = httpClient;
        _dbContext = dbContext;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task SendAsync(
        Guid toAccountId,
        string title,
        string body,
        NotificationType type,
        Guid targetId,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var deviceTokenCollection = _dbContext.GetCollection<DeviceToken>(CollectionName.DeviceToken);

        var deviceTokens = await deviceTokenCollection
            .Find(x => x.AccountId == toAccountId && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        // An account with no live token is the ordinary case for anyone who declined the
        // permission or turned the setting off. Not a failure, and nothing to log.
        if(deviceTokens.Count == 0)
            return;

        // Enums serialize as integers under the default options, which is what the client's
        // `NotificationType: Int` decodes. The client owns the type-to-screen mapping, so
        // the payload stays a description of the event rather than a navigation instruction.
        string payload = JsonSerializer.Serialize(new
        {
            aps = new
            {
                alert = new { title, body },
                sound = "default"
            },
            notificationId,
            type,
            targetId
        });

        // Sequential: an account holds one or two devices in practice. Task.WhenAll over the
        // tokens is the upgrade if that stops being true.
        foreach(DeviceToken deviceToken in deviceTokens)
        {
            await SendToDeviceAsync(
                deviceTokenCollection,
                deviceToken,
                payload,
                notificationId,
                cancellationToken);
        }
    }

    private async Task SendToDeviceAsync(
        IMongoCollection<DeviceToken> deviceTokenCollection,
        DeviceToken deviceToken,
        string payload,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        (HttpStatusCode status, string? reason) = await PostAsync(
            deviceToken,
            payload,
            notificationId,
            forceRefresh : false,
            cancellationToken);

        // The one failure worth retrying: the fix is local, immediate and cannot loop,
        // because the provider token only regenerates once per 20 minutes.
        if(status == HttpStatusCode.Forbidden && reason == "ExpiredProviderToken")
        {
            (status, reason) = await PostAsync(
                deviceToken,
                payload,
                notificationId,
                forceRefresh : true,
                cancellationToken);
        }

        if(status == HttpStatusCode.OK)
            return;

        // Without this the server pushes at uninstalled apps forever and the dead rows
        // accumulate per account with no upper bound.
        if(IsDeadToken(status, reason))
        {
            await deviceTokenCollection.UpdateOneAsync(
                Builders<DeviceToken>.Filter.Eq(x => x.Id, deviceToken.Id),
                Builders<DeviceToken>.Update
                    .Set(x => x.IsDeleted, true)
                    .Set(x => x.DeletedAt, DateTime.UtcNow),
                cancellationToken : cancellationToken);

            _logger.LogInformation(
                "Retired device token {DeviceTokenId} for {AccountId}: {Reason}",
                deviceToken.Id,
                deviceToken.AccountId,
                reason);

            return;
        }

        _logger.LogWarning(
            "APNs rejected notification {NotificationId}: {Status} {Reason}",
            notificationId,
            (int)status,
            reason);
    }

    private async Task<(HttpStatusCode Status, string? Reason)> PostAsync(
        DeviceToken deviceToken,
        string payload,
        Guid notificationId,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        string host = deviceToken.Environment == ApnsEnvironment.Production
            ? ProductionHost
            : SandboxHost;

        // Set here rather than on the client: HttpClient.DefaultRequestVersion only seeds the
        // requests it builds itself, so a message constructed here keeps HttpRequestMessage's
        // own 1.1 default. APNs answers HTTP/1.1 by closing the connection, which surfaces as
        // "The response ended prematurely" rather than as anything naming the version.
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{host}/3/device/{deviceToken.Token}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "bearer",
            _tokenProvider.Current(forceRefresh));

        request.Headers.TryAddWithoutValidation("apns-topic", _tokenProvider.BundleId);
        request.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        request.Headers.TryAddWithoutValidation("apns-priority", "10");
        request.Headers.TryAddWithoutValidation(
            "apns-expiration",
            DateTimeOffset.UtcNow.Add(Expiration).ToUnixTimeSeconds().ToString());
        // Echoed back by APNs, so one log search on the notification id follows a delivery
        // across the row, the SignalR push and the APNs response.
        request.Headers.TryAddWithoutValidation("apns-id", notificationId.ToString());

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if(response.IsSuccessStatusCode)
            return (response.StatusCode, null);

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        return (response.StatusCode, ReadReason(responseBody));
    }

    private static bool IsDeadToken(HttpStatusCode status, string? reason)
    {
        if(status == HttpStatusCode.Gone)
            return true;

        return status == HttpStatusCode.BadRequest
            && reason is "BadDeviceToken" or "DeviceTokenNotForTopic";
    }

    private static string? ReadReason(string responseBody)
    {
        if(string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);

            return document.RootElement.TryGetProperty("reason", out JsonElement reason)
                ? reason.GetString()
                : null;
        }
        catch(JsonException)
        {
            return null;
        }
    }
}
