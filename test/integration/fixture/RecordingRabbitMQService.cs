namespace integration;

// test-strategy.md §3.3 point 4 puts "the published message" in the handler tier's own
// assertions, not only in the §3.5 worker tests — a handler that stops publishing is a
// silent regression no worker test would ever see. Wraps the real IRabbitMQService rather
// than replacing it, so the queue consumers registered in IntegrationTestFixture still
// receive every message exactly as production would.
//
// Singleton so every scope in the test host writes into the same log; ConcurrentBag
// because tests in this collection run concurrently against shared containers (§4) and
// each other's publishes land here too — tests must filter by the ids they seeded, never
// by count or order.
public sealed class PublishedMessageLog
{
    private readonly System.Collections.Concurrent.ConcurrentBag<PublishedMessage> _messages = new();

    public void Record(string queueName, object message) => _messages.Add(new PublishedMessage(queueName, message));

    public IReadOnlyCollection<PublishedMessage> Messages => _messages;
}

public sealed record PublishedMessage(string QueueName, object Message);

internal sealed class RecordingRabbitMQService : IRabbitMQService
{
    private readonly IRabbitMQService _inner;
    private readonly PublishedMessageLog _log;

    public RecordingRabbitMQService(IRabbitMQService inner, PublishedMessageLog log)
    {
        _inner = inner;
        _log = log;
    }

    public Task PublishAsync<T>(string queueName, T message, CancellationToken cancellationToken)
    {
        _log.Record(queueName, message!);

        return _inner.PublishAsync(queueName, message, cancellationToken);
    }

    public Task ConsumeAsync<T>(
        string queueName,
        Func<T, string> getIdempotencyKey,
        Func<T, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken) =>
        _inner.ConsumeAsync(queueName, getIdempotencyKey, onMessage, cancellationToken);
}
