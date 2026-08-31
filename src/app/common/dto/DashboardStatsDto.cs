namespace app;

public sealed record DashboardStatsDto(
    long ActiveAccountAmount,
    long ActivePostAmount,
    long ActiveCommentAmount);
