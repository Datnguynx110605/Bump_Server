using AutoMapper;
using domain;

namespace app;

public sealed class PostProfile : Profile
{
    public PostProfile()
    {
        CreateMap<Post, PostDto>();
        CreateMap<Post, PostSummaryDto>();
    }
}
