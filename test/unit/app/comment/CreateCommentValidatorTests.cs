namespace unit;

public class CreateCommentValidatorTests
{
    private static readonly CreateCommentValidator Validator = new();

    private static CreateCommentCommand Command(
        Guid? postId = null,
        string? content = null,
        List<(Stream stream, string fileName)>? images = null) =>
        new(postId ?? Guid.NewGuid(), content, images);

    [Fact]
    public void EmptyPostId_IsRejected()
    {
        var result = Validator.Validate(Command(postId: Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "PostId is required");
    }

    [Fact]
    public void ContentOverTwoThousandChars_IsRejected()
    {
        var result = Validator.Validate(Command(content: new string('a', 2001)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Content is too long, maximum length is 2000 characters");
    }

    [Fact]
    public void AtLimit_Passes()
    {
        var result = Validator.Validate(Command(content: new string('a', 2000)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ImageWithNoFileName_IsRejected()
    {
        var result = Validator.Validate(Command(images: [(new MemoryStream(), "")]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "FileName is required");
    }

    [Fact]
    public void NoContentAndNoImages_Passes()
    {
        var result = Validator.Validate(Command());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(Command(content: "nice place"));

        Assert.True(result.IsValid);
    }
}
