using System.Security.Cryptography;
using app;

namespace infra;

internal sealed class OtpService : IOtpService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly IRedisService _redisService;

    public OtpService(IRedisService redisService)
    {
        _redisService = redisService;
    }

    public async Task<string> GenerateAsync(string identifier, CancellationToken cancellationToken)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var entry = new OtpEntry(code, 0, DateTime.UtcNow.Add(Ttl));

        await _redisService.SetAsync(BuildKey(identifier), entry, Ttl, cancellationToken);

        return code;
    }
    
    public async Task<bool> VerifyAsync(string identifier, string code, CancellationToken cancellationToken)
    {
        var key = BuildKey(identifier);
        var entry = await _redisService.GetAndRemoveAsync<OtpEntry>(key, cancellationToken);
        var remaining = entry is null ? TimeSpan.Zero : entry.ExpiresAt - DateTime.UtcNow;

        if (entry is null || remaining <= TimeSpan.Zero)
            return false;

        if (entry.Code == code)
            return true;

        var attempts = entry.Attempts + 1;
        if (attempts < MaxAttempts)
            await _redisService.SetAsync(key, entry with { Attempts = attempts }, remaining, cancellationToken);

        return false;
    }

    private static string BuildKey(string identifier) => $"otp:{identifier}";

    private sealed record OtpEntry(string Code, int Attempts, DateTime ExpiresAt);
}
