using AutoMapper;
using domain;

namespace app;

public sealed class FollowingProfile : Profile
{
    public FollowingProfile()
    {
        CreateMap<Following, FollowingDto>();
    }
}
