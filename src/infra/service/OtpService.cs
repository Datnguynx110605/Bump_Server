using System.Security.Cryptography;
using app;

namespace infra;

public sealed class OtpService : IOtpService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly IRedisService _redisService;

    public OtpService(IRedisService redisService)
    {
        _redisService = redisService;
    }

    public async Task<string> GenerateAsync(
        string identifier, 
        CancellationToken cancellationToken)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var entry = new OtpEntry(
            code, 
            0, 
            DateTime.UtcNow.Add(Ttl));

        await _redisService.SetAsync(
            BuildKey(identifier), 
            entry, 
            Ttl, 
            cancellationToken);

        return code;
    }
    
    public async Task<bool> VerifyAsync(
        string identifier, 
        string code, 
        CancellationToken cancellationToken)
    {
        var key = BuildKey(identifier);
        var entry = await _redisService.GetAsync<OtpEntry>(
            key, 
            cancellationToken);
        var remaining = entry is null ? TimeSpan.Zero : entry.ExpiresAt - DateTime.UtcNow;

        if(entry is null || remaining <= TimeSpan.Zero)
            return false;

        // A correct code is deliberately left in place until its own 10 minute TTL rather
        // than deleted here. Deleting made a lost response unrecoverable: the caller never
        // received the registration token the code was exchanged for, and the code that
        // would have earned another was already gone. The attempt cap below is what stops
        // brute force; re-presenting a code the caller already proved they hold does not
        // widen anything.
        if(entry.Code == code)
            return true;

        var attempts = entry.Attempts + 1;
        if(attempts < MaxAttempts)
            await _redisService.SetAsync(
                key, 
                entry with { Attempts = attempts }, 
                remaining, 
                cancellationToken);
        else
            await _redisService.RemoveAsync(
                key, 
                cancellationToken);

        return false;
    }

    private static string BuildKey(string identifier) => $"otp:{identifier}";

    private sealed record OtpEntry(
        string Code, 
        int Attempts, 
        DateTime ExpiresAt);
}
