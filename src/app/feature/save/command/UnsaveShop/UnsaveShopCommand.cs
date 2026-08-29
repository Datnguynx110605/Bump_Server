using FluentResults;
using MediatR;

namespace app;

public sealed record UnsaveShopCommand(Guid shopId)
    : IRequest<Result>;
