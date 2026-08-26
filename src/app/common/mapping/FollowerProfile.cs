using AutoMapper;
using domain;

namespace app;

public sealed class FollowerProfile : Profile
{
    public FollowerProfile()
    {
        CreateMap<Follower, FollowerDto>();
    }
}
