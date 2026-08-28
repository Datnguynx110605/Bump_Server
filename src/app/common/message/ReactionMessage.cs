namespace app;

public sealed record ReactionMessage(
    Guid MessageId, 
    Guid AccountId, 
    Guid TargetId);
