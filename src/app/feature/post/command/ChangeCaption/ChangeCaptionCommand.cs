using FluentResults;
using MediatR;

namespace app;

public sealed record ChangeCaptionCommand(
    Guid postId,
    string caption)
    : IRequest<Result>;
