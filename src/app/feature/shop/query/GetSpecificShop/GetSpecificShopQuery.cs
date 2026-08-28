using FluentResults;
using MediatR;

namespace app;

public sealed record GetSpecificShopQuery(Guid shopId)
    : IRequest<Result<ShopDto>>;
