using domain;
using FluentResults;
using MediatR;

namespace app;

public sealed record ChangeRateCommand(
    Guid postId,
    Amount rate)
    : IRequest<Result>;
