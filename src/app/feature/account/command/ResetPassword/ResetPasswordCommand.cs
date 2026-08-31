using FluentResults;
using MediatR;

namespace app;

public sealed record ResetPasswordCommand(
    string registrationToken,
    string newPassword)
    : IRequest<Result>;
