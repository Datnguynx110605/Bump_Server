using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentResults;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace integration;

// test-strategy.md §3.5's transport contract: tested once here against the real
// RabbitMQService/QueueConsumerHost rather than per worker, since every worker rides the
// same ConsumeAsync. Each test gets its own queue name so tests never interfere with each
// other or with the twelve production consumers the fixture also has running.
[Collection(IntegrationCollection.Name)]
public class RabbitMQTransportContractTests
{
    private const string RetryCountHeader = "x-retry-count";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly IntegrationTestFixture _fixture;

    public RabbitMQTransportContractTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record TransportTestMessage(Guid Id, string Payload);

    private static string UniqueQueueName([CallerMemberName] string caller = "") =>
        $"test-transport-{caller}-{Guid.NewGuid():N}";

    // Mirrors the three declarations RabbitMQService.ConsumeAsync makes, so a message can be
    // published — including straight into .retry or .dlq — before a consumer ever attaches.
    // Idempotent against ConsumeAsync's own declare: same name, same arguments.
    private static async Task EnsureQueuesAsync(IChannel channel, string queueName)
    {
        await channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueDeclareAsync($"{queueName}.dlq", durable: true, exclusive: false, autoDelete: false);
        await channel.QueueDeclareAsync(
            $"{queueName}.retry",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = (int)RetryDelay.TotalMilliseconds,
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = queueName
            });
    }

    private static async Task PublishRawAsync(IChannel channel, string queueName, byte[] body, int retryCount)
    {
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queueName,
            mandatory: false,
            basicProperties: new BasicProperties
            {
                Persistent = true,
                Headers = new Dictionary<string, object?> { [RetryCountHeader] = retryCount }
            },
            body: body);
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while(DateTime.UtcNow < deadline)
        {
            if(await condition())
                return true;

            await Task.Delay(50);
        }

        return false;
    }

    private static async Task<BasicGetResult?> WaitForMessageAsync(IChannel channel, string queueName)
    {
        BasicGetResult? result = null;
        await WaitUntilAsync(async () =>
        {
            result = await channel.BasicGetAsync(queueName, autoAck: true);
            return result is not null;
        });

        return result;
    }

    private async Task StopConsumingAsync(CancellationTokenSource cts, Task consumeTask)
    {
        cts.Cancel();
        try
        {
            await consumeTask;
        }
        catch(OperationCanceledException)
        {
        }
    }

    [Fact]
    public async Task Consume_ValidMessage_InvokesTheHandler()
    {
        using var scope = _fixture.Services.CreateScope();
        var rabbitMQService = scope.ServiceProvider.GetRequiredService<IRabbitMQService>();
        var connection = scope.ServiceProvider.GetRequiredService<IConnection>();
        string queueName = UniqueQueueName();
        await using var channel = await connection.CreateChannelAsync();
        await EnsureQueuesAsync(channel, queueName);

        List<TransportTestMessage> received = [];
        using var cts = new CancellationTokenSource();
        var consumeTask = rabbitMQService.ConsumeAsync<TransportTestMessage>(
            queueName,
            m => m.Id.ToString(),
            (m, _) => { received.Add(m); return Task.CompletedTask; },
            cts.Token);

        var message = new TransportTestMessage(Guid.NewGuid(), "hello");
        await rabbitMQService.PublishAsync(queueName, message, CancellationToken.None);

        Assert.True(await WaitUntilAsync(() => Task.FromResult(received.Count == 1)));
        Assert.Equal(message, received[0]);

        await StopConsumingAsync(cts, consumeTask);
    }

    [Fact]
    public async Task Consume_RedeliveryOfSameMessageIdAndPayload_SkipsTheHandler()
    {
        using var scope = _fixture.Services.CreateScope();
        var rabbitMQService = scope.ServiceProvider.GetRequiredService<IRabbitMQService>();
        var connection = scope.ServiceProvider.GetRequiredService<IConnection>();
        string queueName = UniqueQueueName();
        await using var channel = await connection.CreateChannelAsync();
        await EnsureQueuesAsync(channel, queueName);

        int invocations = 0;
        using var cts = new CancellationTokenSource();
        var consumeTask = rabbitMQService.ConsumeAsync<TransportTestMessage>(
            queueName,
            m => m.Id.ToString(),
            (_, _) => { Interlocked.Increment(ref invocations); return Task.CompletedTask; },
            cts.Token);

        var message = new TransportTestMessage(Guid.NewGuid(), "same-payload");
        await rabbitMQService.PublishAsync(queueName, message, CancellationToken.None);
        Assert.True(await WaitUntilAsync(() => Task.FromResult(Volatile.Read(ref invocations) == 1)));

        // Identical id and identical bytes: the idempotency key already recorded this
        // payload's hash, so this redelivery must be acked without a second Handle call.
        await rabbitMQService.PublishAsync(queueName, message, CancellationToken.None);
        await Task.Delay(500);

        Assert.Equal(1, Volatile.Read(ref invocations));

        await StopConsumingAsync(cts, consumeTask);
    }

    [Fact]
    public async Task Consume_RedeliveryOfSameMessageIdDifferentPayload_DeadLettersRatherThanReprocessing()
    {
        using var scope = _fixture.Services.CreateScope();
        var rabbitMQService = scope.ServiceProvider.GetRequiredService<IRabbitMQService>();
        var connection = scope.ServiceProvider.GetRequiredService<IConnection>();
        string queueName = UniqueQueueName();
        await using var channel = await connection.CreateChannelAsync();
        await EnsureQueuesAsync(channel, queueName);

        int invocations = 0;
        using var cts = new CancellationTokenSource();
        var consumeTask = rabbitMQService.ConsumeAsync<TransportTestMessage>(
            queueName,
            m => m.Id.ToString(),
            (_, _) => { Interlocked.Increment(ref invocations); return Task.CompletedTask; },
            cts.Token);

        Guid id = Guid.NewGuid();
        await rabbitMQService.PublishAsync(queueName, new TransportTestMessage(id, "payload-1"), CancellationToken.None);
        Assert.True(await WaitUntilAsync(() => Task.FromResult(Volatile.Read(ref invocations) == 1)));

        await rabbitMQService.PublishAsync(queueName, new TransportTestMessage(id, "payload-2"), CancellationToken.None);

        var deadLettered = await WaitForMessageAsync(channel, $"{queueName}.dlq");

        Assert.NotNull(deadLettered);
        Assert.Contains("payload-2", Encoding.UTF8.GetString(deadLettered!.Body.Span));
        // The mismatched redelivery goes straight to the dead-letter queue instead of
        // reaching the handler a second time.
        Assert.Equal(1, Volatile.Read(ref invocations));

        await StopConsumingAsync(cts, consumeTask);
    }

    [Fact]
    public async Task Consume_ABodyThatDeserializesToNull_GoesStraightToTheDeadLetterQueue()
    {
        using var scope = _fixture.Services.CreateScope();
        var rabbitMQService = scope.ServiceProvider.GetRequiredService<IRabbitMQService>();
        var connection = scope.ServiceProvider.GetRequiredService<IConnection>();
        string queueName = UniqueQueueName();
        await using var channel = await connection.CreateChannelAsync();
        await EnsureQueuesAsync(channel, queueName);
        // "null" is syntactically valid JSON, so this is the `message is null` branch's own
        // explicit dead-letter — not the generic catch every other failure in this file goes
        // through. Genuinely malformed JSON (e.g. "not valid json") throws JsonException
        // instead, which the generic catch retries like any other handler exception rather
        // than dead-lettering it directly — read from RabbitMQService while writing this
        // test; test-strategy.md's "an undeserializable body goes straight to the dlq" is
        // only true for this literal-null case, corrected there alongside this test.
        await PublishRawAsync(channel, queueName, "null"u8.ToArray(), retryCount: 0);

        bool handlerInvoked = false;
        using var cts = new CancellationTokenSource();
        var consumeTask = rabbitMQService.ConsumeAsync<TransportTestMessage>(
            queueName,
            m => m.Id.ToString(),
            (_, _) => { handlerInvoked = true; return Task.CompletedTask; },
            cts.Token);

        var deadLettered = await WaitForMessageAsync(channel, $"{queueName}.dlq");

        Assert.NotNull(deadLettered);
        Assert.False(handlerInvoked);

        await StopConsumingAsync(cts, consumeTask);
    }

    [Fact]
    public async Task Consume_MalformedJsonBody_IsRetriedLikeAnyOtherHandlerException_NotDeadLetteredDirectly()
    {
        using var scope = _fixture.Services.CreateScope();
        var rabbitMQService = scope.ServiceProvider.GetRequiredService<IRabbitMQService>();
        var connection = scope.ServiceProvider.GetRequiredService<IConnection>();
        string queueName = UniqueQueueName();
        await using var channel = await connection.CreateChannelAsync();
        await EnsureQueuesAsync(channel, queueName);
        await PublishRawAsync(channel, queueName, "not valid json"u8.ToArray(), retryCount: 0);

        using var cts = new CancellationTokenSource();
        // The handler is never reached here: JsonSerializer.Deserialize throws on genuinely
        // malformed JSON before onMessage is ever called, so what this delegate does is
        // irrelevant to the outcome.
        var consumeTask = rabbitMQService.ConsumeAsync<TransportTestMessage>(
            queueName,
            m => m.Id.ToString(),
            (_, _) => Task.CompletedTask,
            cts.Token);

        var retried = await WaitForMessageAsync(channel, $"{queueName}.retry");

        Assert.NotNull(retried);
        Assert.Equal(1, retried!.BasicProperties.Headers![RetryCountHeader]);

        await StopConsumingAsync(cts, consumeTask);
    }

    [Fact]
    public async Task Consume_ThrowingHandler_RepublishesToTheRetryQueue_WithRetryCountIncremented()
    {
        using var scope = _fixture.Services.CreateScope();
        var rabbitMQService = scope.ServiceProvider.GetRequiredService<IRabbitMQService>();
        var connection = scope.ServiceProvider.GetRequiredService<IConnection>();
        string queueName = UniqueQueueName();
        await using var channel = await connection.CreateChannelAsync();
        await EnsureQueuesAsync(channel, queueName);
        await rabbitMQService.PublishAsync(
            queueName,
            new TransportTestMessage(Guid.NewGuid(), "boom"),
            CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var consumeTask = rabbitMQService.ConsumeAsync<TransportTestMessage>(
            queueName,
            m => m.Id.ToString(),
            (_, _) => throw new InvalidOperationException("simulated handler failure"),
            cts.Token);

        var retried = await WaitForMessageAsync(channel, $"{queueName}.retry");

        Assert.NotNull(retried);
        Assert.Equal(1, retried!.BasicProperties.Headers![RetryCountHeader]);

        await StopConsumingAsync(cts, consumeTask);
    }

    [Fact]
    public async Task Consume_ThrowingHandler_ThirdAttempt_GoesToTheDeadLetterQueueInsteadOfRetry()
    {
        using var scope = _fixture.Services.CreateScope();
        var rabbitMQService = scope.ServiceProvider.GetRequiredService<IRabbitMQService>();
        var connection = scope.ServiceProvider.GetRequiredService<IConnection>();
        string queueName = UniqueQueueName();
        await using var channel = await connection.CreateChannelAsync();
        await EnsureQueuesAsync(channel, queueName);

        // Publishing straight in with x-retry-count already at 2 skips waiting through two
        // real 30s retry-queue TTL cycles: this delivery IS the third attempt as far as
        // RabbitMQService's MaxAttempts check is concerned.
        var message = new TransportTestMessage(Guid.NewGuid(), "boom");
        await PublishRawAsync(channel, queueName, JsonSerializer.SerializeToUtf8Bytes(message), retryCount: 2);

        using var cts = new CancellationTokenSource();
        var consumeTask = rabbitMQService.ConsumeAsync<TransportTestMessage>(
            queueName,
            m => m.Id.ToString(),
            (_, _) => throw new InvalidOperationException("simulated handler failure"),
            cts.Token);

        var deadLettered = await WaitForMessageAsync(channel, $"{queueName}.dlq");

        Assert.NotNull(deadLettered);

        var stillRetried = await channel.BasicGetAsync($"{queueName}.retry", autoAck: true);
        Assert.Null(stillRetried);

        await StopConsumingAsync(cts, consumeTask);
    }

    [Fact]
    public async Task QueueConsumerHost_FailedResult_IsAckedAndLoggedRatherThanRetriedOrDeadLettered()
    {
        using var scope = _fixture.Services.CreateScope();
        var rabbitMQService = scope.ServiceProvider.GetRequiredService<IRabbitMQService>();
        var connection = scope.ServiceProvider.GetRequiredService<IConnection>();
        var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();
        string queueName = UniqueQueueName();
        await using var channel = await connection.CreateChannelAsync();
        await EnsureQueuesAsync(channel, queueName);

        var host = new QueueConsumerHost<TransportTestMessage>(
            queueName,
            m => m.Id,
            (_, _, _) => Task.FromResult(Result.Fail("simulated refusal")),
            _fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            _fixture.Services.GetRequiredService<ILogger<QueueConsumerHost<TransportTestMessage>>>());
        await host.StartAsync(CancellationToken.None);

        try
        {
            var message = new TransportTestMessage(Guid.NewGuid(), "refused");
            await rabbitMQService.PublishAsync(queueName, message, CancellationToken.None);

            // A failed Result is treated as success by RabbitMQService: the idempotency key
            // is what proves the message was acked on the normal path rather than left
            // unprocessed, retried or dead-lettered.
            string idempotencyKey = $"rabbitmq:idempotency:{queueName}:{message.Id}";
            Assert.True(await WaitUntilAsync(
                async () => await redisService.GetAsync<string>(idempotencyKey, CancellationToken.None) is not null));

            var retried = await channel.BasicGetAsync($"{queueName}.retry", autoAck: true);
            var deadLettered = await channel.BasicGetAsync($"{queueName}.dlq", autoAck: true);

            Assert.Null(retried);
            Assert.Null(deadLettered);

            // test-strategy.md §7.3: this warning is the only trace such a message ever
            // leaves, so it has to carry QueueName, MessageId and the error.
            var warning = _fixture.LogEvents.Events.FirstOrDefault(e =>
                e.RenderMessage().Contains(queueName) &&
                e.RenderMessage().Contains(message.Id.ToString()) &&
                e.RenderMessage().Contains("simulated refusal"));
            Assert.NotNull(warning);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }
}
