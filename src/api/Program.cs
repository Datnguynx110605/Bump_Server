using api;
using infra;
using Serilog;
using Serilog.Templates;

// One line of JSON per event, in the shape a hosting platform's log explorer parses: a `message`
// field, a `level` field in the debug/info/warn/error vocabulary, and every Serilog property
// flattened alongside them so each stays queryable on its own. Declared here rather than in
// appsettings.json because the bootstrap logger below needs the identical shape — everything
// logged before UseSerilog takes over is startup-phase, which is exactly what gets read when a
// deploy crash-loops, and two copies of the template would drift.
const string LogTemplate =
    "{ {timestamp: @t, level: if @l = 'Information' then 'info' else if @l = 'Warning' then 'warn' " +
    "else if @l = 'Error' or @l = 'Fatal' then 'error' else 'debug', message: @m, exception: @x, ..@p} }\n";

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new ExpressionTemplate(LogTemplate))
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(new ExpressionTemplate(LogTemplate)));

    builder.Services.AddApi(builder.Configuration);

    var app = builder.Build();

    app.UseExceptionHandler();

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
    app.MapHub<NotificationHub>("/hubs/notification");
    app.MapHealthChecks("/health");

    app.Run();
}
catch(Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
