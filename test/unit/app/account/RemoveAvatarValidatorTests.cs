namespace unit;

public class RemoveAvatarValidatorTests
{
    private static readonly RemoveAvatarValidator Validator = new();

    [Fact]
    public void EmptyUrl_IsRejected()
    {
        var result = Validator.Validate(new RemoveAvatarCommand(""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Url is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new RemoveAvatarCommand("http://example.com/a.png"));

        Assert.True(result.IsValid);
    }
}
