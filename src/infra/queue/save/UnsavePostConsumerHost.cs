using app;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace infra;

internal sealed class UnsavePostConsumerHost : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UnsavePostConsumerHost> _logger;

    public UnsavePostConsumerHost(
        IServiceScopeFactory scopeFactory,
        ILogger<UnsavePostConsumerHost> logger)
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
            await rabbitMQService.ConsumeAsync<SaveMessage>(
                QueueName.UnsavePost,
                message => message.MessageId.ToString(),
                async (message, cancellationToken) =>
                {
                    using var messageScope = _scopeFactory.CreateScope();
                    var saveService = messageScope.ServiceProvider.GetRequiredService<ISaveService>();
                    var result = await saveService.ProcessUnsavePostAsync(
                        message.AccountId,
                        message.TargetId,
                        cancellationToken);

                    if(result.IsFailed)
                        _logger.LogWarning(
                            "UnsavePost {MessageId} ({AccountId} -> {PostId}) did not apply: {Error}",
                            message.MessageId, message.AccountId, message.TargetId, result.Errors[0].Message);
                },
                stoppingToken);
        }
        catch(Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "UnsavePost consumer stopped unexpectedly");
        }
    }
}
