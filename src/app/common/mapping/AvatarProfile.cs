using AutoMapper;
using domain;

namespace app;

public sealed class AvatarProfile : Profile
{
    public AvatarProfile()
    {
        CreateMap<Avatar, AvatarDto>();
    }
}
