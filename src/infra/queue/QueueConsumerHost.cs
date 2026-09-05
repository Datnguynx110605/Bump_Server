using app;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace infra;

internal sealed class QueueConsumerHost<TMessage> : BackgroundService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

    private readonly string _queueName;
    private readonly Func<TMessage, Guid> _messageId;
    private readonly Func<IServiceProvider, TMessage, CancellationToken, Task<Result>> _handle;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QueueConsumerHost<TMessage>> _logger;

    public QueueConsumerHost(
        string queueName,
        Func<TMessage, Guid> messageId,
        Func<IServiceProvider, TMessage, CancellationToken, Task<Result>> handle,
        IServiceScopeFactory scopeFactory,
        ILogger<QueueConsumerHost<TMessage>> logger)
    {
        _queueName = queueName;
        _messageId = messageId;
        _handle = handle;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var rabbitMQService = scope.ServiceProvider.GetRequiredService<IRabbitMQService>();

                await rabbitMQService.ConsumeAsync<TMessage>(
                    _queueName,
                    message => _messageId(message).ToString(),
                    HandleAsync,
                    stoppingToken);
            }
            catch(Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(
                    ex,
                    "{QueueName} consumer stopped unexpectedly, restarting in {RestartDelaySeconds}s",
                    _queueName,
                    RestartDelay.TotalSeconds);

                await Task.Delay(RestartDelay, stoppingToken);
            }
        }
    }

    private async Task HandleAsync(TMessage message, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var result = await _handle(
            scope.ServiceProvider,
            message,
            cancellationToken);

        if(result.IsFailed)
            _logger.LogWarning(
                "{QueueName} message {MessageId} did not apply: {Error}",
                _queueName,
                _messageId(message),
                result.Errors[0].Message);
    }
}
