namespace app;

public sealed record AccountMessage(
    Guid MessageId,
    Guid AccountId);
