using System.Security.Cryptography;
using System.Text.Json;
using app;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace infra;

public sealed class RabbitMQService : IRabbitMQService
{
    private const int MaxAttempts = 3;
    private const string RetryCountHeader = "x-retry-count";
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromHours(24);

    private readonly IConnection _connection;
    private readonly IRedisService _redisService;

    public RabbitMQService(
        IConnection connection, 
        IRedisService redisService)
    {
        _connection = connection;
        _redisService = redisService;
    }

    public async Task PublishAsync<T>(
        string queueName, 
        T message, 
        CancellationToken cancellationToken)
    {
        using var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await PublishRawAsync(
            channel, 
            queueName, 
            JsonSerializer.SerializeToUtf8Bytes(message), 
            retryCount: 0, 
            cancellationToken);
    }

    public async Task ConsumeAsync<T>(
        string queueName,
        Func<T, string> getIdempotencyKey,
        Func<T, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken)
    {
        var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            queue: DeadLetterQueueName(queueName),
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);
        await channel.BasicQosAsync(
            prefetchSize: 0, 
            prefetchCount: 1, 
            global: false, 
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (sender, args) =>
        {
            var retryCount = GetRetryCount(args.BasicProperties);

            // Everything below — including deserialization and the
            // idempotency lookup, not just onMessage — must stay inside this
            // try/catch. A transient failure in any of it (e.g. Redis briefly
            // unreachable during the idempotency check) would otherwise throw
            // out of this event handler with the message never acked, never
            // retried, and never dead-lettered: it just silently vanishes.
            try
            {
                var message = JsonSerializer.Deserialize<T>(args.Body.Span);
                if(message is null)
                {
                    await PublishRawAsync(
                        channel,
                        DeadLetterQueueName(queueName),
                        args.Body.ToArray(),
                        retryCount,
                        cancellationToken);
                    await channel.BasicAckAsync(
                        args.DeliveryTag,
                        multiple: false,
                        cancellationToken);
                    return;
                }

                var idempotencyKey = BuildIdempotencyKey(
                    queueName,
                    getIdempotencyKey(message));
                var payloadHash = Convert.ToHexString(SHA256.HashData(args.Body.Span));

                var processedHash = await _redisService.GetAsync<string?>(
                    idempotencyKey,
                    cancellationToken);
                if(processedHash is not null)
                {
                    if(processedHash != payloadHash)
                    {
                        await PublishRawAsync(
                            channel,
                            DeadLetterQueueName(queueName),
                            args.Body.ToArray(),
                            retryCount,
                            cancellationToken);
                    }

                    await channel.BasicAckAsync(
                        args.DeliveryTag,
                        multiple: false,
                        cancellationToken);
                    return;
                }

                await onMessage(
                    message,
                    cancellationToken);

                await _redisService.SetAsync(
                    idempotencyKey,
                    payloadHash,
                    IdempotencyTtl,
                    cancellationToken);
                await channel.BasicAckAsync(
                    args.DeliveryTag,
                    multiple: false,
                    cancellationToken);
            }
            catch
            {
                var nextAttempt = retryCount + 1;
                var targetQueue = nextAttempt >= MaxAttempts ? DeadLetterQueueName(queueName) : queueName;

                await PublishRawAsync(
                    channel,
                    targetQueue,
                    args.Body.ToArray(),
                    nextAttempt,
                    cancellationToken);
                await channel.BasicAckAsync(
                    args.DeliveryTag,
                    multiple: false,
                    cancellationToken);
            }
        };

        await channel.BasicConsumeAsync(
            queue: queueName, 
            autoAck: false, 
            consumer: consumer, 
            cancellationToken: cancellationToken);

        try
        {
            await Task.Delay(
                Timeout.Infinite, 
                cancellationToken);
        }
        catch(OperationCanceledException)
        {
            await channel.CloseAsync(CancellationToken.None);
        }
    }

    private static async Task PublishRawAsync(
        IChannel channel, 
        string queueName, 
        byte[] body, 
        int retryCount, 
        CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            Persistent = true,
            Headers = new Dictionary<string, object?> { [RetryCountHeader] = retryCount }
        };

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queueName,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    private static int GetRetryCount(IReadOnlyBasicProperties properties)
    {
        if(properties.Headers is null || !properties.Headers.TryGetValue(RetryCountHeader, out var value))
            return 0;

        return value switch
        {
            int i => i,
            long l => (int)l,
            _ => 0
        };
    }

    private static string DeadLetterQueueName(string queueName) => $"{queueName}.dlq";

    private static string BuildIdempotencyKey(
        string queueName, 
        string idempotencyKey) 
        => $"rabbitmq:idempotency:{queueName}:{idempotencyKey}";
}
