using FluentResults;
using MediatR;

namespace app;

public sealed record ChangeAvatarCommand(
    Stream avatars,
    string fileName)
    : IRequest<Result>;
