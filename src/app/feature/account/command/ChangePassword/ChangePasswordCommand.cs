using FluentResults;
using MediatR;

namespace app;

public sealed record ChangePasswordCommand(
    string currentPassword,
    string newPassword)
    : IRequest<Result>;
