using FluentResults;
using MediatR;

namespace app;

internal sealed class SaveShopHandler
    : IRequestHandler<SaveShopCommand, Result>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IRabbitMQService _rabbitMQService;

    public SaveShopHandler(
        ICurrentIdentityService identityService,
        IRabbitMQService rabbitMQService)
    {
        _identityService = identityService;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result> Handle(
        SaveShopCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        var message = new SaveMessage(
            Guid.NewGuid(),
            identity.Value,
            request.shopId);

        await _rabbitMQService.PublishAsync(
            QueueName.SaveShop,
            message,
            cancellationToken);

        return Result.Ok();
    }
}
