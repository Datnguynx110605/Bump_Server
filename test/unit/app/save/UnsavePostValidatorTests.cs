namespace unit;

public class UnsavePostValidatorTests
{
    private static readonly UnsavePostValidator Validator = new();

    [Fact]
    public void EmptyPostId_IsRejected()
    {
        var result = Validator.Validate(new UnsavePostCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "PostId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new UnsavePostCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
