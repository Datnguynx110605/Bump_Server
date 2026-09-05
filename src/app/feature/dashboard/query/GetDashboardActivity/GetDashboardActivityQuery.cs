using FluentResults;
using MediatR;

namespace app;

public sealed record GetDashboardActivityQuery(int windowHours)
    : IRequest<Result<DashboardActivityDto>>;
