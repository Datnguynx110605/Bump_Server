using FluentResults;
using MediatR;

namespace app;

public sealed record RecoverAccountCommand
    : IRequest<Result>;
