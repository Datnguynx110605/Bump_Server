using api;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddApi(builder.Configuration);

    var app = builder.Build();

    if(app.Environment.IsDevelopment())
        app.MapOpenApi();

    app.UseForwardedHeaders();

    app.UseHttpsRedirection();

    app.UseCorrelationId();

    app.UsePerformance();

    app.UseSerilogRequestLogging();

    app.UseAuthentication();

    app.UseRateLimiter();

    app.UseAuthorization();

    app.MapControllers();

    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
