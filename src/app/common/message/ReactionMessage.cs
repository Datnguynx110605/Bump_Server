namespace app;

// React and unreact share one queue per target type so a rapid react/unreact pair keeps
// its order; IsUndo tells the consumer which way to apply it.
public sealed record ReactionMessage(
    Guid MessageId, 
    Guid AccountId, 
    Guid TargetId,
    bool IsUndo);
