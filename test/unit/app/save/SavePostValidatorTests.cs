namespace unit;

public class SavePostValidatorTests
{
    private static readonly SavePostValidator Validator = new();

    [Fact]
    public void EmptyPostId_IsRejected()
    {
        var result = Validator.Validate(new SavePostCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "PostId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new SavePostCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
