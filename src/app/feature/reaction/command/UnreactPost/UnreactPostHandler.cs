using FluentResults;
using MediatR;

namespace app;

internal sealed class UnreactPostHandler
    : IRequestHandler<UnreactPostCommand, Result>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRabbitMQService _rabbitMQService;

    public UnreactPostHandler(
        ICurrentIdentityService identityService,
        IRabbitMQService rabbitMQService)
    {
        _identityService = identityService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        UnreactPostCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        var message = new ReactionMessage(
            Guid.NewGuid(), 
            identity.Value, 
            request.postId);

        await _rabbitMQService.PublishAsync(
            QueueName.UnreactPost, 
            message, 
            cancellationToken);

        return Result.Ok();
    }
}
