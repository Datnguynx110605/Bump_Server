using FluentResults;
using MediatR;

namespace app;

internal sealed class ReactCommentHandler
    : IRequestHandler<ReactCommentCommand, Result>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRabbitMQService _rabbitMQService;

    public ReactCommentHandler(
        ICurrentIdentityService identityService,
        IRabbitMQService rabbitMQService)
    {
        _identityService = identityService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        ReactCommentCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        var message = new ReactionMessage(Guid.NewGuid(), identity.Value, request.commentId);

        await _rabbitMQService.PublishAsync(QueueName.ReactComment, message, cancellationToken);

        return Result.Ok();
    }
}
