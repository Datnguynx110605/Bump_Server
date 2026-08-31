using FluentResults;
using MediatR;

namespace app;

public sealed record GetDashboardStatsQuery
    : IRequest<Result<DashboardStatsDto>>;
