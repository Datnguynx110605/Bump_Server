using FluentResults;
using MediatR;

namespace app;

public sealed record CreateReportCommand(
    Guid? postId,
    Guid? commentId,
    Guid? accountId,
    string reason)
    : IRequest<Result<ReportDto>>;
