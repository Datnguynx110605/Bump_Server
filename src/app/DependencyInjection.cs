using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace app;

public static class DependencyInjection
{
    public static IServiceCollection AddApp(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            // Innermost, so validation still short-circuits on the caller's token and only the
            // handler itself runs detached.
            cfg.AddOpenBehavior(typeof(WriteCancellationBehavior<,>));
        });
        services.AddAutoMapper(cfg => {}, typeof(DependencyInjection).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        return services;
    }
}
