namespace unit;

public class RequestNewAccessTokenValidatorTests
{
    private static readonly RequestNewAccessTokenValidator Validator = new();

    [Fact]
    public void EmptyRefreshToken_IsRejected()
    {
        var result = Validator.Validate(new RequestNewAccessTokenCommand(""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "RefreshToken is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new RequestNewAccessTokenCommand("some-token"));

        Assert.True(result.IsValid);
    }
}
