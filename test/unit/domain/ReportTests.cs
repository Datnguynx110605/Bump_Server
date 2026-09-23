namespace unit;

public class ReportTests
{
    [Fact]
    public void Create_EmptyReporterId_IsRejected()
    {
        DomainResult<Report> result = Report.Create(Guid.Empty, null, null, Guid.NewGuid(), "reason");

        Assert.False(result.IsSuccess);
        Assert.Equal("ReportId is required", result.Error);
    }

    [Fact]
    public void Create_WhitespaceReason_IsRejected()
    {
        DomainResult<Report> result = Report.Create(Guid.NewGuid(), null, null, Guid.NewGuid(), "   ");

        Assert.False(result.IsSuccess);
        Assert.Equal("Reason is required", result.Error);
    }

    [Fact]
    public void Create_Valid_Succeeds()
    {
        Guid reporterId = Guid.NewGuid();
        Guid targetAccountId = Guid.NewGuid();

        DomainResult<Report> result = Report.Create(reporterId, null, null, targetAccountId, "spam");

        Assert.True(result.IsSuccess);
        Assert.Equal(reporterId, result.Value!.ReporterId);
        Assert.Equal(targetAccountId, result.Value.AccountId);
        Assert.Equal("spam", result.Value.Reason);
    }
}
