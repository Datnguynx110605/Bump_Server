using System.Diagnostics;

namespace api;

public sealed class PerformanceMiddleware
{
    private const int WarningThresholdMilliseconds = 500;

    private readonly RequestDelegate _next;
    private readonly ILogger<PerformanceMiddleware> _logger;

    public PerformanceMiddleware(
        RequestDelegate next,
        ILogger<PerformanceMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var timer = Stopwatch.StartNew();

        await _next(context);

        timer.Stop();

        if(timer.ElapsedMilliseconds > WarningThresholdMilliseconds)
            _logger.LogWarning(
                "{Method} {Path} took {ElapsedMilliseconds} ms",
                context.Request.Method,
                context.Request.Path,
                timer.ElapsedMilliseconds);
    }
}
