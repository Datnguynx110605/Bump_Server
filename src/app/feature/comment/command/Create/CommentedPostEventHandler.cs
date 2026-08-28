using domain;
using MediatR;

namespace app;

internal sealed class CommentedPostEventHandler
    : INotificationHandler<DomainEventNotification<CommentedPostEvent>>
{
    public Task Handle(
        DomainEventNotification<CommentedPostEvent> notification,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
