namespace unit;

public class ReactPostValidatorTests
{
    private static readonly ReactPostValidator Validator = new();

    [Fact]
    public void EmptyPostId_IsRejected()
    {
        var result = Validator.Validate(new ReactPostCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "PostId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new ReactPostCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
