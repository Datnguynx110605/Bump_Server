using FluentResults;
using MediatR;

namespace app;

internal sealed class UnsavePostHandler
    : IRequestHandler<UnsavePostCommand, Result>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRabbitMQService _rabbitMQService;

    public UnsavePostHandler(
        ICurrentIdentityService identityService,
        IRabbitMQService rabbitMQService)
    {
        _identityService = identityService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        UnsavePostCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        var message = new SaveMessage(
            Guid.NewGuid(),
            identity.Value,
            request.postId);

        await _rabbitMQService.PublishAsync(
            QueueName.UnsavePost,
            message,
            cancellationToken);

        return Result.Ok();
    }
}
