namespace app;

// Block and unblock share one queue so a rapid block/unblock pair keeps its order;
// IsUndo tells the consumer which way to apply it. Everything the feature writes lives
// in the consumer — pushing Blocks in the handler would put the array and the severing
// on two independent channels, which can reorder against each other.
public sealed record BlockMessage(
    Guid MessageId,
    Guid BlockerId,
    Guid BlockedId,
    bool IsUndo);
