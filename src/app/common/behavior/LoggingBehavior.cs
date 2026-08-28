using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace app;

public sealed class LoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        string requestName = typeof(TRequest).Name;

        _logger.LogInformation("Handling {RequestName}", requestName);

        var response = await next(cancellationToken);

        if(response is ResultBase { IsFailed: true } failure)
            _logger.LogWarning(
                "Handled {RequestName} with failure {Errors}",
                requestName,
                string.Join(", ", failure.Errors.Select(x => x.Message)));
        else
            _logger.LogInformation("Handled {RequestName}", requestName);

        return response;
    }
}
