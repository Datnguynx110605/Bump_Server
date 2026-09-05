namespace app;

public sealed record DashboardActivityPointDto(
    DateTime Bucket,
    long AccountAmount,
    long PostAmount,
    long CommentAmount);

public sealed record DashboardActivityDto(
    int WindowHours,
    IReadOnlyList<DashboardActivityPointDto> Points);
