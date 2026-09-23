namespace unit;

public class UnreactCommentValidatorTests
{
    private static readonly UnreactCommentValidator Validator = new();

    [Fact]
    public void EmptyCommentId_IsRejected()
    {
        var result = Validator.Validate(new UnreactCommentCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "CommentId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new UnreactCommentCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
