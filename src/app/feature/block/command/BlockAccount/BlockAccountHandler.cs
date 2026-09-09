using FluentResults;
using MediatR;

namespace app;

internal sealed class BlockAccountHandler
    : IRequestHandler<BlockAccountCommand, Result>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRabbitMQService _rabbitMQService;

    public BlockAccountHandler(
        ICurrentIdentityService identityService,
        IRabbitMQService rabbitMQService)
    {
        _identityService = identityService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        BlockAccountCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        if(identity.Value == request.accountId)
            return Result.Fail("Cannot block yourself");

        var message = new BlockMessage(
            Guid.NewGuid(),
            identity.Value,
            request.accountId,
            IsUndo : false);

        await _rabbitMQService.PublishAsync(
            QueueName.Block,
            message,
            cancellationToken);

        return Result.Ok();
    }
}
