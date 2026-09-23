namespace unit;

public class DeviceTokenTests
{
    [Fact]
    public void Create_EmptyAccountId_IsRejected()
    {
        DomainResult<DeviceToken> result = DeviceToken.Create(Guid.Empty, "token", ApnsEnvironment.Sandbox);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountId is required", result.Error);
    }

    [Fact]
    public void Create_WhitespaceToken_IsRejected()
    {
        DomainResult<DeviceToken> result = DeviceToken.Create(Guid.NewGuid(), "   ", ApnsEnvironment.Sandbox);

        Assert.False(result.IsSuccess);
        Assert.Equal("Token is required", result.Error);
    }

    [Fact]
    public void Create_Valid_Succeeds()
    {
        Guid accountId = Guid.NewGuid();

        DomainResult<DeviceToken> result = DeviceToken.Create(accountId, "token", ApnsEnvironment.Production);

        Assert.True(result.IsSuccess);
        Assert.Equal(accountId, result.Value!.AccountId);
        Assert.Equal("token", result.Value.Token);
        Assert.Equal(ApnsEnvironment.Production, result.Value.Environment);
    }
}
