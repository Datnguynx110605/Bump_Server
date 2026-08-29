using app;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace infra;

internal sealed class FollowConsumerHost : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FollowConsumerHost> _logger;

    public FollowConsumerHost(
        IServiceScopeFactory scopeFactory, 
        ILogger<FollowConsumerHost> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var rabbitMQService = scope.ServiceProvider.GetRequiredService<IRabbitMQService>();

        await rabbitMQService.ConsumeAsync<FollowMessage>(
            QueueName.Follow,
            message => message.MessageId.ToString(),
            async (message, cancellationToken) =>
            {
                using var messageScope = _scopeFactory.CreateScope();
                var followService = messageScope.ServiceProvider.GetRequiredService<IFollowService>();
                var result = await followService.ProcessFollowAsync(
                    message.FollowerId, 
                    message.FollowingId, 
                    cancellationToken);

                if(result.IsFailed)
                    _logger.LogWarning(
                        "Follow {MessageId} ({FollowerId} -> {FollowingId}) did not apply: {Error}",
                        message.MessageId, message.FollowerId, message.FollowingId, result.Errors[0].Message);
            },
            stoppingToken);
    }
}
