namespace app;

// Save and unsave share one queue per target type so a rapid save/unsave pair keeps its
// order; IsUndo tells the consumer which way to apply it.
public sealed record SaveMessage(
    Guid MessageId,
    Guid AccountId,
    Guid TargetId,
    bool IsUndo);
