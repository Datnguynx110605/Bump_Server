using RabbitMQ.Client;

namespace infra;

// One channel for every publish in the process, behind one lock, with each queue declared
// once rather than once per message.
//
// PublishAsync used to open a channel and redeclare the queue every time it sent anything:
// three broker round trips to deliver one message, on the path every reaction, follow, save
// and comment takes. The account delete cascade made it visible — it publishes one message
// per post and per comment in a loop, so deleting an account with 500 posts cost 1,500 round
// trips where 500 would do.
//
// A single channel rather than a pool because BasicPublish without publisher confirms does
// not wait for the broker, so the lock is held across a socket write and nothing else.
// IChannel is not thread-safe in RabbitMQ.Client 7, which is the only reason the lock exists.
//
// ponytail: one channel, serialized. If publish throughput ever shows up in a profile, widen
// this to a small pool — nothing outside this class has to change.
public sealed class RabbitMQPublishChannel : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _declared = new();
    private IChannel? _channel;

    public RabbitMQPublishChannel(IConnection connection)
    {
        _connection = connection;
    }

    public async Task PublishAsync(
        string queueName,
        byte[] body,
        BasicProperties properties,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Recreated rather than reused when the broker has closed it. A channel-level
            // error closes the channel and leaves the connection up, so without this one bad
            // publish would leave every later publish in the process writing to a dead
            // handle. The declared set is cleared with it: a fresh channel has to re-declare.
            if(_channel is null || !_channel.IsOpen)
            {
                if(_channel is not null)
                    await _channel.DisposeAsync();

                _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
                _declared.Clear();
            }

            // Kept, but once per queue per channel instead of once per message. Dropping it
            // entirely would have been fine in steady state — QueueConsumerHost declares
            // every queue at startup — but a publish that beats its consumer to the broker
            // routes to a queue that does not exist yet, and the default exchange discards
            // that silently.
            if(_declared.Add(queueName))
                await _channel.QueueDeclareAsync(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    cancellationToken: cancellationToken);

            await _channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: queueName,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if(_channel is not null)
            await _channel.DisposeAsync();

        _gate.Dispose();
    }
}
