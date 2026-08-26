using domain;
using MediatR;

namespace app;

public sealed class DomainEventNotification<TEvent> : INotification
    where TEvent : Event
{
    public TEvent DomainEvent { get; }

    public DomainEventNotification(TEvent domainEvent)
    {
        DomainEvent = domainEvent;
    }
}
