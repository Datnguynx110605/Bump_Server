namespace app;

// Follow and unfollow share one queue so a rapid follow/unfollow pair keeps its order;
// IsUndo tells the consumer which way to apply it.
public sealed record FollowMessage(
    Guid MessageId, 
    Guid FollowerId, 
    Guid FollowingId,
    bool IsUndo);
