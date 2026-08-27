using FluentResults;
using MediatR;

namespace app;

public sealed record RemoveAvatarCommand(string url)
    : IRequest<Result>;
