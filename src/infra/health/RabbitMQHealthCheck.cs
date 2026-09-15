using app;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace infra;

public sealed class RabbitMQHealthCheck : IHealthCheck
{
    // Every queue RabbitMQService dead-letters to. Nothing consumes these, so without a
    // reading somewhere they accumulate silently — including delete-account and
    // delete-post cascades that the API already answered 202 for.
    private static readonly string[] Queues =
    [
        QueueName.Follow,
        QueueName.Block,
        QueueName.ReactPost,
        QueueName.ReactComment,
        QueueName.CreateComment,
        QueueName.RecalculateShopRate,
        QueueName.SavePost,
        QueueName.SaveShop,
        QueueName.DeleteAccount,
        QueueName.DeletePost,
        QueueName.DeleteComment
    ];

    private readonly IConnection _connection;
    private readonly ILogger<RabbitMQHealthCheck> _logger;
    public RabbitMQHealthCheck(
        IConnection connection,
        ILogger<RabbitMQHealthCheck> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if(!_connection.IsOpen)
            return HealthCheckResult.Unhealthy("RabbitMQ connection is closed");

        await using var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        var dead = new Dictionary<string, object>();

        foreach(string queue in Queues)
        {
            string dlq = $"{queue}.dlq";

            try
            {
                // Passive: reports depth without creating the queue, so a queue no consumer
                // has declared yet is simply absent rather than counted as zero.
                uint count = await channel.MessageCountAsync(dlq, cancellationToken);
                if(count > 0)
                    dead[dlq] = count;
            }
            catch(OperationInterruptedException)
            {
                // MessageCount on a queue that does not exist closes the channel, so the
                // remaining queues cannot be read on it. Nothing is wrong with the broker.
                break;
            }
        }

        // Deliberately Healthy rather than Degraded: this endpoint is the platform's
        // restart probe, and reporting a non-empty DLQ as unhealthy would restart-loop the
        // container over messages that are already parked and harmless. The warning is
        // what makes them visible; the status stays about whether the broker is reachable.
        if(dead.Count > 0)
            _logger.LogWarning(
                "Dead-letter queues are not empty: {DeadLetterDepths}",
                string.Join(", ", dead.Select(x => $"{x.Key}={x.Value}")));

        return HealthCheckResult.Healthy(
            dead.Count > 0 ? "RabbitMQ reachable, dead-letter queues not empty" : "RabbitMQ reachable",
            dead);
    }
}
