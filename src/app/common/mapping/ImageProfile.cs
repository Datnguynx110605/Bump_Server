using AutoMapper;
using domain;

namespace app;

public sealed class ImageProfile : Profile
{
    public ImageProfile()
    {
        CreateMap<Image, ImageDto>();
    }
}
