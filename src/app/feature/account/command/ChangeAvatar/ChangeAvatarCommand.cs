using FluentResults;
using MediatR;

namespace app;

public sealed record ChangeAvatarCommand(
    Stream stream,
    string fileName)
    : IRequest<Result>;
