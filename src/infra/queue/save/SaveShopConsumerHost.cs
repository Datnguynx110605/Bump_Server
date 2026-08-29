using app;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace infra;

internal sealed class SaveShopConsumerHost : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SaveShopConsumerHost> _logger;

    public SaveShopConsumerHost(
        IServiceScopeFactory scopeFactory,
        ILogger<SaveShopConsumerHost> logger)
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
                QueueName.SaveShop,
                message => message.MessageId.ToString(),
                async (message, cancellationToken) =>
                {
                    using var messageScope = _scopeFactory.CreateScope();
                    var saveService = messageScope.ServiceProvider.GetRequiredService<ISaveService>();
                    var result = await saveService.ProcessSaveShopAsync(
                        message.AccountId,
                        message.TargetId,
                        cancellationToken);

                    if(result.IsFailed)
                        _logger.LogWarning(
                            "SaveShop {MessageId} ({AccountId} -> {ShopId}) did not apply: {Error}",
                            message.MessageId, message.AccountId, message.TargetId, result.Errors[0].Message);
                },
                stoppingToken);
        }
        catch(Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "SaveShop consumer stopped unexpectedly");
        }
    }
}
