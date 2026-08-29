using app;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace infra;

internal sealed class RecalculateShopRateConsumerHost : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecalculateShopRateConsumerHost> _logger;

    public RecalculateShopRateConsumerHost(
        IServiceScopeFactory scopeFactory,
        ILogger<RecalculateShopRateConsumerHost> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var rabbitMQService = scope.ServiceProvider.GetRequiredService<IRabbitMQService>();

        await rabbitMQService.ConsumeAsync<RecalculateShopRateMessage>(
            QueueName.RecalculateShopRate,
            message => message.MessageId.ToString(),
            async (message, cancellationToken) =>
            {
                using var messageScope = _scopeFactory.CreateScope();
                var shopRateService = messageScope.ServiceProvider.GetRequiredService<IShopRateService>();
                var result = await shopRateService.ProcessRecalculateShopRateAsync(
                    message.ShopId,
                    cancellationToken);

                if(result.IsFailed)
                    _logger.LogWarning(
                        "RecalculateShopRate {MessageId} ({ShopId}) did not apply: {Error}",
                        message.MessageId, message.ShopId, result.Errors[0].Message);
            },
            stoppingToken);
    }
}
