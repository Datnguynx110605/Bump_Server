namespace unit;

public class AdminDeleteCommentValidatorTests
{
    private static readonly AdminDeleteCommentValidator Validator = new();

    [Fact]
    public void EmptyCommentId_IsRejected()
    {
        var result = Validator.Validate(new AdminDeleteCommentCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "CommentId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new AdminDeleteCommentCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
