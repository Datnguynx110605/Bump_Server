using AutoMapper;
using domain;

namespace app;

public sealed class NotificationProfile : Profile
{
    public NotificationProfile()
    {
        CreateMap<Notification, NotificationSummaryDto>();
    }
}
