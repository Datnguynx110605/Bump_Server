namespace unit;

public class AdminDeletePostValidatorTests
{
    private static readonly AdminDeletePostValidator Validator = new();

    [Fact]
    public void EmptyPostId_IsRejected()
    {
        var result = Validator.Validate(new AdminDeletePostCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "PostId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new AdminDeletePostCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
