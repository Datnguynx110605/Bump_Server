namespace app;

public sealed record SaveMessage(
    Guid MessageId,
    Guid AccountId,
    Guid TargetId);
