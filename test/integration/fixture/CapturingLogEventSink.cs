using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace integration;

// Program.cs's UseSerilog callback already calls .ReadFrom.Services(services), which is
// what lets Serilog resolve a sink registered in the DI container — registering this as a
// singleton ILogEventSink in IntegrationTestFixture is picked up with zero production
// changes. Used by CorrelationIdMiddlewareTests to prove the id CorrelationIdMiddleware
// pushes into Serilog's LogContext actually reaches an event logged from inside the MediatR
// pipeline (LoggingBehavior), not only the response header.
//
// ConcurrentBag and read by content, not by count or order, for the same reason
// PublishedMessageLog is: every test in this collection shares one sink.
public sealed class CapturingLogEventSink : ILogEventSink
{
    private readonly ConcurrentBag<LogEvent> _events = new();

    public void Emit(LogEvent logEvent) => _events.Add(logEvent);

    public IReadOnlyCollection<LogEvent> Events => _events;
}
