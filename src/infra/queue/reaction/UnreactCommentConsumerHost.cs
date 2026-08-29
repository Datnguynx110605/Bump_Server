using app;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace infra;

internal sealed class UnreactCommentConsumerHost : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UnreactCommentConsumerHost> _logger;

    public UnreactCommentConsumerHost(
        IServiceScopeFactory scopeFactory, 
        ILogger<UnreactCommentConsumerHost> logger)
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
            await rabbitMQService.ConsumeAsync<ReactionMessage>(
                QueueName.UnreactComment,
                message => message.MessageId.ToString(),
                async (message, cancellationToken) =>
                {
                    using var messageScope = _scopeFactory.CreateScope();
                    var reactionService = messageScope.ServiceProvider.GetRequiredService<IReactionService>();
                    var result = await reactionService.ProcessUnreactCommentAsync(
                        message.AccountId,
                        message.TargetId,
                        cancellationToken);

                    if(result.IsFailed)
                        _logger.LogWarning(
                            "UnreactComment {MessageId} ({AccountId} -> {CommentId}) did not apply: {Error}",
                            message.MessageId, message.AccountId, message.TargetId, result.Errors[0].Message);
                },
                stoppingToken);
        }
        catch(Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "UnreactComment consumer stopped unexpectedly");
        }
    }
}
