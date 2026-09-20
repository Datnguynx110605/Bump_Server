using System.Security.Cryptography;
using app;

namespace infra;

public sealed class OtpService : IOtpService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    // Every rate limit guarding the OTP endpoints partitions by IP, while the OTP itself is
    // keyed by email and carries its own attempt counter — so minting a fresh code reset the
    // count to zero and nothing bounded how many codes one address could be issued. Three
    // guesses per code against a million values looks safe until you notice generations are
    // unlimited: with modest IP rotation that is hours to brute force a password reset, not
    // years. These two counters are per address and therefore reachable no matter which IP
    // asks, which is the property the edge limits cannot provide. Capping generations is
    // enough on its own: five codes an hour at three guesses each is fifteen values out of a
    // million, so exhausting the space runs to years.
    //
    // There is deliberately no counter on failures. Keying one by email hands anyone who
    // knows an address a way to lock its owner out of password reset by guessing wrong on
    // their behalf — the victim is the only one who loses anything, and they lose the one
    // endpoint they need most at exactly the moment they need it.
    private const int MaxGenerationsPerWindow = 5;
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromHours(1);

    private readonly IRedisService _redisService;

    public OtpService(IRedisService redisService)
    {
        _redisService = redisService;
    }

    public async Task<string?> GenerateAsync(
        string identifier, 
        CancellationToken cancellationToken)
    {
        var generations = await _redisService.IncrementAsync(
            GenerationKey(identifier),
            ThrottleWindow,
            cancellationToken);

        if(generations > MaxGenerationsPerWindow)
            return null;

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

    // The window is tumbling, not sliding: the key carries the UTC hour, so it rolls over on
    // the hour no matter how hard the address is being hit. IncrementAsync re-applies the TTL
    // on every call — correct for the list-version counters it also serves, wrong here, where
    // a refreshed TTL would mean anyone who knows an address could hold its owner out of
    // password reset indefinitely just by requesting codes on their behalf. Naming the window
    // in the key gets a fixed window without changing that shared behaviour. The TTL is then
    // only there to collect the key after nothing reads it.
    private static string GenerationKey(string identifier) =>
        $"otp:gen:{identifier}:{DateTime.UtcNow:yyyyMMddHH}";

    private sealed record OtpEntry(
        string Code, 
        int Attempts, 
        DateTime ExpiresAt);
}
