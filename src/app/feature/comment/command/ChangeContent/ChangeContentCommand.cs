using FluentResults;
using MediatR;

namespace app;

public sealed record ChangeContentCommand(
    Guid commentId,
    string content)
    : IRequest<Result>;
