namespace api;

public static class PerformanceMiddlewareExtensions
{
    public static IApplicationBuilder UsePerformance(this IApplicationBuilder app)
    {
        return app.UseMiddleware<PerformanceMiddleware>();
    }
}
