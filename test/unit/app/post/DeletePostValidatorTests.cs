namespace unit;

public class DeletePostValidatorTests
{
    private static readonly DeletePostValidator Validator = new();

    [Fact]
    public void EmptyPostId_IsRejected()
    {
        var result = Validator.Validate(new DeletePostCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "PostId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new DeletePostCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
