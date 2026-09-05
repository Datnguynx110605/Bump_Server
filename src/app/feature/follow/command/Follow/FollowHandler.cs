using FluentResults;
using MediatR;

namespace app;

internal sealed class FollowHandler
    : IRequestHandler<FollowCommand, Result>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRabbitMQService _rabbitMQService;

    public FollowHandler(
        ICurrentIdentityService identityService,
        IRabbitMQService rabbitMQService)
    {
        _identityService = identityService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        FollowCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        if(identity.Value == request.followingId)
            return Result.Fail("Cannot follow yourself");

        var message = new FollowMessage(
            Guid.NewGuid(), 
            identity.Value, 
            request.followingId);

        await _rabbitMQService.PublishAsync(
            QueueName.Follow, 
            message, 
            cancellationToken);

        return Result.Ok();
    }
}
