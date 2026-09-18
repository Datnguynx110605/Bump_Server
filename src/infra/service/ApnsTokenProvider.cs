using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace infra;

// Owns the APNs configuration and mints the ES256 provider token every request
// authenticates with. A singleton because the token cache has to be shared: Apple rejects a
// provider that regenerates its token more than once every 20 minutes, which a per-request
// instance would do.
public sealed class ApnsTokenProvider
{
    // Apple requires the token be refreshed no more often than every 20 minutes and no
    // less often than every 60. 45 sits clear of both edges.
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(20);

    private readonly string _keyId;
    private readonly string _teamId;
    private readonly string _privateKey;
    private readonly object _gate = new();

    // The apns-topic header. Missing, every push is answered 400 MissingTopic, which is a
    // configuration fault rather than a dead token, so nothing would prune it and the
    // warning would repeat per notification forever. Validated here with the rest.
    public string BundleId { get; }

    private string? _token;
    private DateTime _issuedAt;

    // Reads its configuration eagerly so a missing key fails the deploy rather than every
    // push. A push failure is swallowed by design in NotificationService, so a silently
    // misconfigured APNs would otherwise look exactly like a working one.
    public ApnsTokenProvider(IConfiguration configuration)
    {
        _keyId = Required(configuration, "Apns:KeyId");
        _teamId = Required(configuration, "Apns:TeamId");
        _privateKey = Required(configuration, "Apns:PrivateKey");
        BundleId = Required(configuration, "Apns:BundleId");
    }

    public string Current(bool forceRefresh = false)
    {
        lock(_gate)
        {
            if(_token is not null && !IsStale(forceRefresh))
                return _token;

            using ECDsa key = ECDsa.Create();
            key.ImportFromPem(Encoding.UTF8.GetString(Convert.FromBase64String(_privateKey)));

            string header = Base64UrlEncoder.Encode($"{{\"alg\":\"ES256\",\"kid\":\"{_keyId}\"}}");

            string claims = Base64UrlEncoder.Encode(
                $"{{\"iss\":\"{_teamId}\",\"iat\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}");

            byte[] signature = key.SignData(
                Encoding.UTF8.GetBytes($"{header}.{claims}"),
                HashAlgorithmName.SHA256);

            // JWS ES256 is the IEEE P1363 r||s pair, exactly 64 bytes for P-256. A DER
            // signature is 70-72 bytes and variable, and APNs answers one with an opaque
            // 403 that gives no hint the encoding is the problem.
            if(signature.Length != 64)
                throw new InvalidOperationException("APNs requires a 64-byte P1363 signature, not DER");

            _token = $"{header}.{claims}.{Base64UrlEncoder.Encode(signature)}";
            _issuedAt = DateTime.UtcNow;

            return _token;
        }
    }

    // A forced refresh still respects Apple's 20-minute floor. Without it, a burst of
    // ExpiredProviderToken responses would mint a token per push and earn a
    // TooManyProviderTokenUpdates instead.
    private bool IsStale(bool forceRefresh)
    {
        TimeSpan age = DateTime.UtcNow - _issuedAt;

        return forceRefresh ? age > MinimumInterval : age > Lifetime;
    }

    private static string Required(IConfiguration configuration, string key)
    {
        string? value = configuration[key];

        if(string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{key} is not configured");

        return value;
    }
}
