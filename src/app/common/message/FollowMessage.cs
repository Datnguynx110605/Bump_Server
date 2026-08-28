namespace app;

public sealed record FollowMessage(
    Guid MessageId, 
    Guid FollowerId, 
    Guid FollowingId);
