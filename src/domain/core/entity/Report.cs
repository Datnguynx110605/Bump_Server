namespace domain;

public sealed class Report : BaseEntity
{
    public Guid ReporterId { get; private set; }
    public Guid? PostId { get; private set; }
    public Guid? CommentId { get; private set; }
    public Guid? AccountId { get; private set; }
    public string Reason { get; private set; }
    private Report() {}
    private Report(
        Guid reporterId,
        Guid? postId,
        Guid? commentId,
        Guid? accountId,
        string reason)
    {
        ReporterId = reporterId;
        PostId = postId;
        CommentId = commentId;
        AccountId = accountId;
        Reason = reason;
    }

    public static DomainResult<Report> Create(
        Guid reporterId,
        Guid? postId,
        Guid? commentId,
        Guid? accountId,
        string reason)
    {
        if(reporterId == Guid.Empty)
            return DomainResult<Report>.Failure("ReportId is required");

        if(string.IsNullOrWhiteSpace(reason))
            return DomainResult<Report>.Failure("Reason is required");

        Report report = new Report(
            reporterId,
            postId,
            commentId,
            accountId,
            reason);

        return DomainResult<Report>.Success(report);
    }
}
