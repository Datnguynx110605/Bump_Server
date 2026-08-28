using System.Security.Cryptography;
using System.Text;

namespace domain;

public sealed  class RefreshToken : BaseEntity
{
    public Guid AccountId { get; private set; }
    public string Token { get; private set; }
    public bool IsRevoked { get; private set; } = false;
    public DateTime ExpiryDate { get; private set; } = DateTime.UtcNow.AddDays(7);
    private RefreshToken() {}
    private RefreshToken(Guid accountId)
    {
        AccountId = accountId;
        Token = GenerateToken();
    }

    public static DomainResult<RefreshToken> Create(Guid accountId)
    {
        if(accountId == Guid.Empty)
            return DomainResult<RefreshToken>.Failure("AccountId is required");

        RefreshToken refreshToken = new RefreshToken(accountId);

        return DomainResult<RefreshToken>.Success(refreshToken);
    }

    public DomainResult Revoke()
    {
        if(IsRevoked)
            return DomainResult.Failure("Refresh token has been revoked");

        IsRevoked = true;

        return DomainResult.Success();
    }

    public DomainResult Validate()
    {
        if(IsRevoked)
            return DomainResult.Failure("Refresh token has been revoked");

        if(ExpiryDate < DateTime.UtcNow)
            return DomainResult.Failure("Expired");

        return DomainResult.Success();
    }

    private string GenerateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }
}
