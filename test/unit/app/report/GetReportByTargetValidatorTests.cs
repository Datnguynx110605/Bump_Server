namespace unit;

public class GetReportByTargetValidatorTests
{
    private static readonly GetReportByTargetValidator Validator = new();

    [Fact]
    public void TargetOutOfEnum_IsRejected()
    {
        var result = Validator.Validate(new GetReportByTargetQuery((ReportTarget)99, Guid.NewGuid(), new CursorPageRequest(null, 20)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Target is invalid");
    }

    [Fact]
    public void EmptyTargetId_IsRejected()
    {
        var result = Validator.Validate(new GetReportByTargetQuery(ReportTarget.Post, Guid.Empty, new CursorPageRequest(null, 20)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "TargetId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new GetReportByTargetQuery(ReportTarget.Post, Guid.NewGuid(), new CursorPageRequest(null, 20)));

        Assert.True(result.IsValid);
    }
}
