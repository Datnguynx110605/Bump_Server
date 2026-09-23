namespace unit;

public class UnreactPostValidatorTests
{
    private static readonly UnreactPostValidator Validator = new();

    [Fact]
    public void EmptyPostId_IsRejected()
    {
        var result = Validator.Validate(new UnreactPostCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "PostId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new UnreactPostCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
