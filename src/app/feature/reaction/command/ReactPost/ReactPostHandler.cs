using FluentResults;
using MediatR;

namespace app;

internal sealed class ReactPostHandler
    : IRequestHandler<ReactPostCommand, Result>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRabbitMQService _rabbitMQService;

    public ReactPostHandler(
        ICurrentIdentityService identityService,
        IRabbitMQService rabbitMQService)
    {
        _identityService = identityService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        ReactPostCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var message = new ReactionMessage(
            Guid.NewGuid(), 
            identity.Value, 
            request.postId);

        await _rabbitMQService.PublishAsync(
            QueueName.ReactPost, 
            message, 
            cancellationToken);

        return Result.Ok();
    }
}
