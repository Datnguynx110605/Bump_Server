using System.Reflection;

namespace unit;

public class RefreshTokenTests
{
    // ExpiryDate has no domain method to backdate it — BaseEntity-style clock injection is
    // out of scope here (test-strategy.md §8), so the expired branch is reached by setting
    // the private auto-property directly rather than waiting seven days.
    private static void Backdate(RefreshToken token, DateTime expiryDate)
    {
        PropertyInfo property = typeof(RefreshToken).GetProperty(nameof(RefreshToken.ExpiryDate))!;
        property.SetValue(token, expiryDate);
    }

    [Fact]
    public void Create_EmptyAccountId_IsRejected()
    {
        DomainResult<RefreshToken> result = RefreshToken.Create(Guid.Empty);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountId is required", result.Error);
    }

    [Fact]
    public void Create_Valid_Succeeds()
    {
        Guid accountId = Guid.NewGuid();

        DomainResult<RefreshToken> result = RefreshToken.Create(accountId);

        Assert.True(result.IsSuccess);
        Assert.Equal(accountId, result.Value!.AccountId);
        Assert.False(string.IsNullOrEmpty(result.Value.Token));
        Assert.False(result.Value.IsRevoked);
        Assert.InRange(result.Value.ExpiryDate, DateTime.UtcNow.AddDays(6.9), DateTime.UtcNow.AddDays(7.1));
    }

    [Fact]
    public void Revoke_Fresh_Succeeds()
    {
        RefreshToken token = RefreshToken.Create(Guid.NewGuid()).Value!;

        DomainResult result = token.Revoke();

        Assert.True(result.IsSuccess);
        Assert.True(token.IsRevoked);
    }

    [Fact]
    public void Revoke_AlreadyRevoked_Refuses()
    {
        RefreshToken token = RefreshToken.Create(Guid.NewGuid()).Value!;
        token.Revoke();

        DomainResult result = token.Revoke();

        Assert.False(result.IsSuccess);
        Assert.Equal("Refresh token has been revoked", result.Error);
    }

    [Fact]
    public void Validate_FreshAndUnrevoked_Succeeds()
    {
        RefreshToken token = RefreshToken.Create(Guid.NewGuid()).Value!;

        DomainResult result = token.Validate();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_Revoked_Refuses()
    {
        RefreshToken token = RefreshToken.Create(Guid.NewGuid()).Value!;
        token.Revoke();

        DomainResult result = token.Validate();

        Assert.False(result.IsSuccess);
        Assert.Equal("Refresh token has been revoked", result.Error);
    }

    [Fact]
    public void Validate_Expired_Refuses()
    {
        RefreshToken token = RefreshToken.Create(Guid.NewGuid()).Value!;
        Backdate(token, DateTime.UtcNow.AddSeconds(-1));

        DomainResult result = token.Validate();

        Assert.False(result.IsSuccess);
        Assert.Equal("Expired", result.Error);
    }
}
