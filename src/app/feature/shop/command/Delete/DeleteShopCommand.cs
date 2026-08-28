using FluentResults;
using MediatR;

namespace app;

public sealed record DeleteShopCommand(Guid shopId)
    : IRequest<Result>;
