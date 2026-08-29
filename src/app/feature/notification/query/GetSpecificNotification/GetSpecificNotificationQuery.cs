using FluentResults;
using MediatR;

namespace app;

public sealed record GetSpecificNotificationQuery(Guid notificationId)
    : IRequest<Result<NotificationSummaryDto>>;
