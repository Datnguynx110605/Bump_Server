using AutoMapper;
using domain;

namespace app;

public sealed class ShopProfile : Profile
{
    public ShopProfile()
    {
        CreateMap<Shop, ShopDto>();
        CreateMap<Shop, ShopSummaryDto>();
    }
}
