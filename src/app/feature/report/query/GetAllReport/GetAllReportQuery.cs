using FluentResults;
using MediatR;

namespace app;

public sealed record GetAllReportQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<ReportDto>>>;
