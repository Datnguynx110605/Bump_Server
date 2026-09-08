using FluentResults;
using MediatR;

namespace app;

internal sealed class UnreactCommentHandler
    : IRequestHandler<UnreactCommentCommand, Result>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRabbitMQService _rabbitMQService;

    public UnreactCommentHandler(
        ICurrentIdentityService identityService,
        IRabbitMQService rabbitMQService)
    {
        _identityService = identityService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        UnreactCommentCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var message = new ReactionMessage(
            Guid.NewGuid(), 
            identity.Value, 
            request.commentId,
            IsUndo : true);

        await _rabbitMQService.PublishAsync(
            QueueName.ReactComment, 
            message, 
            cancellationToken);

        return Result.Ok();
    }
}
