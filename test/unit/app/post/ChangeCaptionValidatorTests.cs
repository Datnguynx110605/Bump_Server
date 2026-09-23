namespace unit;

public class ChangeCaptionValidatorTests
{
    private static readonly ChangeCaptionValidator Validator = new();

    [Fact]
    public void EmptyPostId_IsRejected()
    {
        var result = Validator.Validate(new ChangeCaptionCommand(Guid.Empty, "hello"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "PostId is required");
    }

    [Fact]
    public void NullCaption_IsRejected()
    {
        var result = Validator.Validate(new ChangeCaptionCommand(Guid.NewGuid(), null!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Caption is required");
    }

    [Fact]
    public void CaptionOverTwoThousandChars_IsRejected()
    {
        var result = Validator.Validate(new ChangeCaptionCommand(Guid.NewGuid(), new string('a', 2001)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Caption is too long, maximum length is 2000 characters");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new ChangeCaptionCommand(Guid.NewGuid(), "hello"));

        Assert.True(result.IsValid);
    }
}
