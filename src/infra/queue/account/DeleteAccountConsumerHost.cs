using app;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace infra;

internal sealed class DeleteAccountConsumerHost : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeleteAccountConsumerHost> _logger;

    public DeleteAccountConsumerHost(
        IServiceScopeFactory scopeFactory,
        ILogger<DeleteAccountConsumerHost> logger)
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
            await rabbitMQService.ConsumeAsync<AccountMessage>(
                QueueName.DeleteAccount,
                message => message.MessageId.ToString(),
                async (message, cancellationToken) =>
                {
                    using var messageScope = _scopeFactory.CreateScope();
                    var deleteAccountService = messageScope.ServiceProvider.GetRequiredService<IDeleteAccountService>();
                    var result = await deleteAccountService.ProcessAfterDeleteAsync(
                        message.AccountId,
                        cancellationToken);

                    if(result.IsFailed)
                        _logger.LogWarning(
                            "DeleteAccount {MessageId} ({AccountId}) did not apply: {Error}",
                            message.MessageId, message.AccountId, result.Errors[0].Message);
                },
                stoppingToken);
        }
        catch(Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "DeleteAccount consumer stopped unexpectedly");
        }
    }
}
