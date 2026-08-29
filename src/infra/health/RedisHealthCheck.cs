using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace infra;

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    public RedisHealthCheck(IConnectionMultiplexer connectionMultiplexer)
    {
        _connectionMultiplexer = connectionMultiplexer;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await _connectionMultiplexer.GetDatabase().PingAsync();

        return HealthCheckResult.Healthy();
    }
}
