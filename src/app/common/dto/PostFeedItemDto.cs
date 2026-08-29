namespace app;

public sealed record PostFeedItemDto(PostSummaryDto Post, bool IsReacted, bool IsSaved);
