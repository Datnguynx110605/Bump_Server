using app;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace infra;

internal sealed class UnfollowConsumerHost : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UnfollowConsumerHost> _logger;

    public UnfollowConsumerHost(
        IServiceScopeFactory scopeFactory, 
        ILogger<UnfollowConsumerHost> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var rabbitMQService = scope.ServiceProvider.GetRequiredService<IRabbitMQService>();

        try
        {
            await rabbitMQService.ConsumeAsync<FollowMessage>(
                QueueName.Unfollow,
                message => message.MessageId.ToString(),
                async (message, cancellationToken) =>
                {
                    using var messageScope = _scopeFactory.CreateScope();
                    var followService = messageScope.ServiceProvider.GetRequiredService<IFollowService>();
                    var result = await followService.ProcessUnfollowAsync(
                        message.FollowerId,
                        message.FollowingId,
                        cancellationToken);

                    if(result.IsFailed)
                        _logger.LogWarning(
                            "Unfollow {MessageId} ({FollowerId} -> {FollowingId}) did not apply: {Error}",
                            message.MessageId, message.FollowerId, message.FollowingId, result.Errors[0].Message);
                },
                stoppingToken);
        }
        catch(Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Unfollow consumer stopped unexpectedly");
        }
    }
}
