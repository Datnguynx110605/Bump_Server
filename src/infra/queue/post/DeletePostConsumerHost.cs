using app;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace infra;

internal sealed class DeletePostConsumerHost : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeletePostConsumerHost> _logger;

    public DeletePostConsumerHost(
        IServiceScopeFactory scopeFactory,
        ILogger<DeletePostConsumerHost> logger)
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
            await rabbitMQService.ConsumeAsync<PostMessage>(
                QueueName.DeletePost,
                message => message.MessageId.ToString(),
                async (message, cancellationToken) =>
                {
                    using var messageScope = _scopeFactory.CreateScope();
                    var deletePostService = messageScope.ServiceProvider.GetRequiredService<IDeletePostService>();
                    var result = await deletePostService.ProcessAfterDeleteAsync(
                        message.PostId,
                        cancellationToken);

                    if(result.IsFailed)
                        _logger.LogWarning(
                            "DeletePost {MessageId} ({PostId}) did not apply: {Error}",
                            message.MessageId, message.PostId, result.Errors[0].Message);
                },
                stoppingToken);
        }
        catch(Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "DeletePost consumer stopped unexpectedly");
        }
    }
}
