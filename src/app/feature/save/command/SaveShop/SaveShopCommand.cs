using FluentResults;
using MediatR;

namespace app;

public sealed record SaveShopCommand(Guid shopId)
    : IRequest<Result>;
