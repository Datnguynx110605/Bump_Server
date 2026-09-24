namespace unit;

public class CreateReportValidatorTests
{
    private static readonly CreateReportValidator Validator = new();

    [Fact]
    public void NoTarget_IsRejected()
    {
        var result = Validator.Validate(new CreateReportCommand(null, null, null, "reason"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "A report must name exactly one of PostId, CommentId or AccountId");
    }

    [Fact]
    public void TwoTargets_IsRejected()
    {
        var result = Validator.Validate(new CreateReportCommand(Guid.NewGuid(), Guid.NewGuid(), null, "reason"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "A report must name exactly one of PostId, CommentId or AccountId");
    }

    [Fact]
    public void EmptyReason_IsRejected()
    {
        var result = Validator.Validate(new CreateReportCommand(Guid.NewGuid(), null, null, ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Reason is required");
    }

    [Fact]
    public void ReasonOver2000Chars_IsRejected()
    {
        var result = Validator.Validate(new CreateReportCommand(Guid.NewGuid(), null, null, new string('a', 2001)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Reason is too long, maximum length is 2000 characters");
    }

    [Fact]
    public void EmptyPostId_IsRejected()
    {
        var result = Validator.Validate(new CreateReportCommand(Guid.Empty, null, null, "reason"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "PostId is invalid");
    }

    [Fact]
    public void EmptyCommentId_IsRejected()
    {
        var result = Validator.Validate(new CreateReportCommand(null, Guid.Empty, null, "reason"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "CommentId is invalid");
    }

    [Fact]
    public void EmptyAccountId_IsRejected()
    {
        var result = Validator.Validate(new CreateReportCommand(null, null, Guid.Empty, "reason"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "AccountId is invalid");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new CreateReportCommand(Guid.NewGuid(), null, null, "reason"));

        Assert.True(result.IsValid);
    }
}
