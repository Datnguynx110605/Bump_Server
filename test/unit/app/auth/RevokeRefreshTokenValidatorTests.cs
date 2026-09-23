namespace unit;

public class RevokeRefreshTokenValidatorTests
{
    private static readonly RevokeRefreshTokenValidator Validator = new();

    [Fact]
    public void EmptyGuid_IsRejected()
    {
        var result = Validator.Validate(new RevokeRefreshTokenCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "RefreshTokenId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new RevokeRefreshTokenCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
