using FluentResults;
using MediatR;

namespace app;

public sealed record GetMyNotificationQuery(CursorPageRequest pageRequest)
    : IRequest<Result<CursorPageResult<NotificationSummaryDto>>>;
