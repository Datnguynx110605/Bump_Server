namespace app;

// Carries only the account id, never the new name or avatar. The worker reads the current
// values itself, which is what makes two renames in quick succession converge on the latest
// one whatever order the messages are consumed in, and what makes a redelivery a harmless
// re-apply rather than a resurrection of a superseded value.
public sealed record ProfileMessage(
    Guid MessageId,
    Guid AccountId);
