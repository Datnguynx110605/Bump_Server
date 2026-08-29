using app;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace infra;

internal sealed class CreateCommentConsumerHost : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CreateCommentConsumerHost> _logger;

    public CreateCommentConsumerHost(
        IServiceScopeFactory scopeFactory,
        ILogger<CreateCommentConsumerHost> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var rabbitMQService = scope.ServiceProvider.GetRequiredService<IRabbitMQService>();

        await rabbitMQService.ConsumeAsync<CommentMessage>(
            QueueName.CreateComment,
            message => message.MessageId.ToString(),
            async (message, cancellationToken) =>
            {
                using var messageScope = _scopeFactory.CreateScope();
                var commentService = messageScope.ServiceProvider.GetRequiredService<ICommentService>();
                var result = await commentService.ProcessCreateCommentAsync(
                    message.AccountId,
                    message.CommentId,
                    cancellationToken);

                if(result.IsFailed)
                    _logger.LogWarning(
                        "CreateComment {MessageId} ({AccountId} -> {CommentId}) did not apply: {Error}",
                        message.MessageId, message.AccountId, message.CommentId, result.Errors[0].Message);
            },
            stoppingToken);
    }
}
