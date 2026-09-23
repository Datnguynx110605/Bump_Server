namespace unit;

public class ChangeContentValidatorTests
{
    private static readonly ChangeContentValidator Validator = new();

    [Fact]
    public void EmptyCommentId_IsRejected()
    {
        var result = Validator.Validate(new ChangeContentCommand(Guid.Empty, "new content"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "CommentId is required");
    }

    [Fact]
    public void NullContent_IsRejected()
    {
        var result = Validator.Validate(new ChangeContentCommand(Guid.NewGuid(), null!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Content is required");
    }

    [Fact]
    public void ContentOverTwoThousandChars_IsRejected()
    {
        var result = Validator.Validate(new ChangeContentCommand(Guid.NewGuid(), new string('a', 2001)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Content is too long, maximum length is 2000 characters");
    }

    [Fact]
    public void EmptyStringContent_Passes()
    {
        var result = Validator.Validate(new ChangeContentCommand(Guid.NewGuid(), ""));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new ChangeContentCommand(Guid.NewGuid(), "updated"));

        Assert.True(result.IsValid);
    }
}
