using MediatR;
using Microsoft.Extensions.Logging;

namespace unit;

// test-strategy.md §7.3: PerformanceBehavior has no injected clock (§4/§8 — introducing one
// is a refactor, not a test change), so proving the 500ms threshold means an actual delay.
public class PerformanceBehaviorTests
{
    private sealed record TestCommand : IRequest<int>;

    private sealed class RecordingLogger : ILogger<PerformanceBehavior<TestCommand, int>>
    {
        public bool WarningLogged { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if(logLevel == LogLevel.Warning)
                WarningLogged = true;
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }

    [Fact]
    public async Task Handle_AboveThreshold_LogsAWarning()
    {
        var logger = new RecordingLogger();
        var behavior = new PerformanceBehavior<TestCommand, int>(logger);

        var result = await behavior.Handle(
            new TestCommand(),
            async ct => { await Task.Delay(550, ct); return 1; },
            CancellationToken.None);

        Assert.Equal(1, result);
        Assert.True(logger.WarningLogged);
    }

    [Fact]
    public async Task Handle_BelowThreshold_DoesNotLog()
    {
        var logger = new RecordingLogger();
        var behavior = new PerformanceBehavior<TestCommand, int>(logger);

        await behavior.Handle(new TestCommand(), _ => Task.FromResult(1), CancellationToken.None);

        Assert.False(logger.WarningLogged);
    }
}
