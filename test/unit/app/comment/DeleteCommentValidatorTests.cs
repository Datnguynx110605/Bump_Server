namespace unit;

public class DeleteCommentValidatorTests
{
    private static readonly DeleteCommentValidator Validator = new();

    [Fact]
    public void EmptyCommentId_IsRejected()
    {
        var result = Validator.Validate(new DeleteCommentCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "CommentId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new DeleteCommentCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
