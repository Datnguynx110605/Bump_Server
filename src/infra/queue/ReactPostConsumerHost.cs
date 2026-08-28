using app;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace infra;

internal sealed class ReactPostConsumerHost : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReactPostConsumerHost> _logger;

    public ReactPostConsumerHost(
        IServiceScopeFactory scopeFactory, 
        ILogger<ReactPostConsumerHost> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var rabbitMQService = scope.ServiceProvider.GetRequiredService<IRabbitMQService>();

        await rabbitMQService.ConsumeAsync<ReactionMessage>(
            QueueName.ReactPost,
            message => message.MessageId.ToString(),
            async (message, cancellationToken) =>
            {
                using var messageScope = _scopeFactory.CreateScope();
                var reactionService = messageScope.ServiceProvider.GetRequiredService<IReactionService>();
                var result = await reactionService.ProcessReactPostAsync(
                    message.AccountId, 
                    message.TargetId, 
                    cancellationToken);

                if(result.IsFailed)
                    _logger.LogWarning(
                        "ReactPost {MessageId} ({AccountId} -> {PostId}) did not apply: {Error}",
                        message.MessageId, message.AccountId, message.TargetId, result.Errors[0].Message);
            },
            stoppingToken);
    }
}
