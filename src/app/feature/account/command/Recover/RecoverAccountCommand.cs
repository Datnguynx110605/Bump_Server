using FluentResults;
using MediatR;

namespace app;

public sealed record RecoverAccountCommand(string registrationToken)
    : IRequest<Result>;
