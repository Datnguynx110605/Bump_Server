using domain;
using MediatR;

namespace app;

internal sealed class PostCreatedEventHandler
    : INotificationHandler<DomainEventNotification<PostCreatedEvent>>
{
    public Task Handle(
        DomainEventNotification<PostCreatedEvent> notification,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
