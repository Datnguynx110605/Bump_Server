using System.Security.Cryptography;
using app;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace infra;

public sealed class RegistrationService : IRegistrationService
{
    private const string Purpose = "RegistrationToken";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly IDataProtector _dataProtector;
    private readonly IRedisService _redisService;

    public RegistrationService(
        IDataProtectionProvider dataProtectionProvider, 
        IRedisService redisService)
    {
        _dataProtector = dataProtectionProvider.CreateProtector(Purpose);
        _redisService = redisService;
    }

    public async Task<string> GenerateTokenAsync(
        string email, 
        CancellationToken cancellationToken)
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var protectedEmail = _dataProtector.Protect(email);

        await _redisService.SetAsync(
            BuildKey(token), 
            protectedEmail, 
            Ttl, 
            cancellationToken);

        return token;
    }

    // Reads without deleting. The token is single-use, but it is only spent once the
    // work it authorises has committed - see ConsumeTokenAsync. Deleting on read burned
    // the token on every failure after this line (a domain validation error, a Mongo
    // throw), leaving the caller with no account and no way back but a fresh OTP.
    public async Task<string?> ExtractEmailAsync(
        string registrationToken, 
        CancellationToken cancellationToken)
    {
        var key = BuildKey(registrationToken);
        var protectedEmail = await _redisService.GetAsync<string>(
            key, 
            cancellationToken);

        if(protectedEmail is null)
            return null;

        try
        {
            return _dataProtector.Unprotect(protectedEmail);
        }
        catch(CryptographicException)
        {
            return null;
        }
    }

    // Called by handlers after their write commits. A token left behind by an
    // uncommitted attempt expires on its own 10 minute TTL.
    public Task ConsumeTokenAsync(
        string registrationToken, 
        CancellationToken cancellationToken) =>
        _redisService.RemoveAsync(
            BuildKey(registrationToken), 
            cancellationToken);

    private static string BuildKey(string token) => $"registration:{token}";
}
