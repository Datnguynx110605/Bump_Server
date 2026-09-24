using System.Reflection;
using api;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace unit;

// test-strategy.md §6: small, cheap checks that guard rules currently held only by
// convention. The third row (every IRequest/IRequest<> in app ends in Command or Query) is
// already pinned by WriteCancellationBehaviorTests.EveryRequestTypeInAppAssembly_....
public class ArchitectureTests
{
    [Fact]
    public void Domain_HasNoProjectOrPackageReferences()
    {
        var referenced = typeof(Account).Assembly.GetReferencedAssemblies();

        Assert.All(referenced, assemblyName => Assert.True(
            assemblyName.Name is not null &&
                (assemblyName.Name.StartsWith("System.", StringComparison.Ordinal) ||
                    assemblyName.Name is "netstandard" or "mscorlib"),
            $"domain unexpectedly references {assemblyName.Name}"));
    }

    [Fact]
    public void App_ReferencesDomain_ButNoInfraOrApiAssembly()
    {
        var referencedNames = typeof(app.DependencyInjection).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        // A positive control: without this, a typo'd forbidden-name check below could never
        // fail, since app→infra/api not existing proves nothing about the check itself.
        Assert.Contains("domain", referencedNames);
        Assert.DoesNotContain("infra", referencedNames);
        Assert.DoesNotContain("api", referencedNames);
    }

    [Fact]
    public void EveryCommandAndQuery_HasARegisteredHandler()
    {
        var services = new ServiceCollection();
        services.AddApp();

        var requestTypes = typeof(app.DependencyInjection).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Select(t => new
            {
                Type = t,
                RequestInterface = t.GetInterfaces().FirstOrDefault(i =>
                    i == typeof(IRequest) ||
                    (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            })
            .Where(x => x.RequestInterface is not null)
            .ToList();

        Assert.NotEmpty(requestTypes);

        foreach(var request in requestTypes)
        {
            Type handlerType = request.RequestInterface == typeof(IRequest)
                ? typeof(IRequestHandler<>).MakeGenericType(request.Type)
                : typeof(IRequestHandler<,>).MakeGenericType(request.Type, request.RequestInterface!.GetGenericArguments()[0]);

            Assert.True(
                services.Any(descriptor => descriptor.ServiceType == handlerType),
                $"{request.Type.FullName} has no handler registered for {handlerType}");
        }
    }

    [Fact]
    public void EveryController_HasAClassLevelAuthorizeAttribute()
    {
        var controllerTypes = typeof(api.DependencyInjection).Assembly
            .GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(controllerTypes);

        Assert.All(controllerTypes, t => Assert.True(
            t.GetCustomAttribute<AuthorizeAttribute>(inherit: true) is not null,
            $"{t.FullName} has no class-level [Authorize] — the auth tier is attribute-held, and a " +
            "controller without one defaults every action inside it to anonymous access"));
    }
}
