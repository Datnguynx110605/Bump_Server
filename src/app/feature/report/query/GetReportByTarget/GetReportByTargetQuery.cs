using FluentResults;
using MediatR;

namespace app;

public sealed record GetReportByTargetQuery(
    ReportTarget target,
    Guid targetId,
    CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<ReportDto>>>;
