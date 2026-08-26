using AutoMapper;
using domain;

namespace app;

public sealed class RefreshTokenProfile : Profile
{
    public RefreshTokenProfile()
    {
        CreateMap<RefreshToken, RefreshTokenDto>();
    }
}
