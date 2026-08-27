using FluentResults;
using MediatR;

namespace app;

public sealed record ChangeEmailCommand(string registrationToken)
    : IRequest<Result>;
