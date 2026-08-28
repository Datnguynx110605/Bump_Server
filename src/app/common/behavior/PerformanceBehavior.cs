using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace app;

public sealed class PerformanceBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int WarningThresholdMilliseconds = 500;
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();

        var response = await next(cancellationToken);

        timer.Stop();

        if(timer.ElapsedMilliseconds > WarningThresholdMilliseconds)
            _logger.LogWarning(
                "{RequestName} took {ElapsedMilliseconds} ms",
                typeof(TRequest).Name,
                timer.ElapsedMilliseconds);

        return response;
    }
}
