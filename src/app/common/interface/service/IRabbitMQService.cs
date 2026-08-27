namespace app;

public interface IRabbitMQService
{
    Task PublishAsync<T>(string queueName, T message, CancellationToken cancellationToken);

    Task ConsumeAsync<T>(
        string queueName,
        Func<T, string> getIdempotencyKey,
        Func<T, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken);
}
