namespace app;

public sealed record PostMessage(
    Guid MessageId,
    Guid PostId);
