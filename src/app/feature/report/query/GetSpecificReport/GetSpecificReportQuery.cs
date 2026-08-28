using FluentResults;
using MediatR;

namespace app;

public sealed record GetSpecificReportQuery(Guid reportId)
    : IRequest<Result<ReportDto>>;
