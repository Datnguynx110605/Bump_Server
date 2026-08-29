using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace infra;

public sealed class RabbitMQHealthCheck : IHealthCheck
{
    private readonly IConnection _connection;
    public RabbitMQHealthCheck(IConnection connection)
    {
        _connection = connection;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if(!_connection.IsOpen)
            return HealthCheckResult.Unhealthy("RabbitMQ connection is closed");

        using var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        return HealthCheckResult.Healthy();
    }
}
