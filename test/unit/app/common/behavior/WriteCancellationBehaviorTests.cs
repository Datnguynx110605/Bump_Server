using FluentResults;
using MediatR;

namespace unit;

public class WriteCancellationBehaviorTests
{
    private sealed record TestCommand(int Value) : IRequest<Result>;
    private sealed record TestQuery(int Value) : IRequest<Result>;

    [Fact]
    public async Task Command_ReceivesCancellationNone_EvenWhenCallerTokenAlreadyCancelled()
    {
        var behavior = new WriteCancellationBehavior<TestCommand, Result>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var received = CancellationToken.None;
        Task<Result> Next(CancellationToken ct)
        {
            received = ct;
            return Task.FromResult(Result.Ok());
        }

        await behavior.Handle(new TestCommand(1), Next, cts.Token);

        Assert.Equal(CancellationToken.None, received);
    }

    [Fact]
    public async Task Query_ReceivesCallersToken_Unchanged()
    {
        var behavior = new WriteCancellationBehavior<TestQuery, Result>();
        using var cts = new CancellationTokenSource();
        var received = CancellationToken.None;
        Task<Result> Next(CancellationToken ct)
        {
            received = ct;
            return Task.FromResult(Result.Ok());
        }

        await behavior.Handle(new TestQuery(1), Next, cts.Token);

        Assert.Equal(cts.Token, received);
    }

    [Fact]
    public void EveryRequestTypeInAppAssembly_NameEndsInCommandOrQuery()
    {
        var requestTypes = typeof(WriteCancellationBehavior<,>).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(t => t.GetInterfaces().Any(i =>
                i == typeof(IRequest) ||
                (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))))
            .ToList();

        Assert.NotEmpty(requestTypes);
        Assert.All(requestTypes, t => Assert.True(
            t.Name.EndsWith("Command", StringComparison.Ordinal) || t.Name.EndsWith("Query", StringComparison.Ordinal),
            $"{t.FullName} does not end in Command or Query"));
    }
}
