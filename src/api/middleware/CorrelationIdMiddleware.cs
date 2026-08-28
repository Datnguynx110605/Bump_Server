using System.Text.RegularExpressions;
using Serilog.Context;

namespace api;

public sealed partial class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-ID";
    private const int MaxLength = 64;

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = TryGetInboundCorrelationId(context, out string? inbound)
            ? inbound
            : Guid.NewGuid().ToString();

        context.Response.Headers[HeaderName] = correlationId;

        using(LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }

    private static bool TryGetInboundCorrelationId(HttpContext context, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? correlationId)
    {
        correlationId = null;

        if(!context.Request.Headers.TryGetValue(HeaderName, out var value))
            return false;

        string candidate = value.ToString();
        if(candidate.Length == 0 || candidate.Length > MaxLength || !AllowedCharacters().IsMatch(candidate))
            return false;

        correlationId = candidate;
        return true;
    }

    [GeneratedRegex("^[a-zA-Z0-9-]+$")]
    private static partial Regex AllowedCharacters();
}
