using FluentResults;
using MediatR;

namespace app;

internal sealed class SavePostHandler
    : IRequestHandler<SavePostCommand, Result>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRabbitMQService _rabbitMQService;

    public SavePostHandler(
        ICurrentIdentityService identityService,
        IRabbitMQService rabbitMQService)
    {
        _identityService = identityService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        SavePostCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var message = new SaveMessage(
            Guid.NewGuid(),
            identity.Value,
            request.postId,
            IsUndo : false);

        await _rabbitMQService.PublishAsync(
            QueueName.SavePost,
            message,
            cancellationToken);

        return Result.Ok();
    }
}
