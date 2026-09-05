using FluentResults;
using MediatR;

namespace app;

internal sealed class UnsaveShopHandler
    : IRequestHandler<UnsaveShopCommand, Result>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRabbitMQService _rabbitMQService;

    public UnsaveShopHandler(
        ICurrentIdentityService identityService,
        IRabbitMQService rabbitMQService)
    {
        _identityService = identityService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        UnsaveShopCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

        var message = new SaveMessage(
            Guid.NewGuid(),
            identity.Value,
            request.shopId);

        await _rabbitMQService.PublishAsync(
            QueueName.UnsaveShop,
            message,
            cancellationToken);

        return Result.Ok();
    }
}
