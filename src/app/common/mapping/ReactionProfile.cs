using AutoMapper;
using domain;

namespace app;

public sealed class ReactionProfile : Profile
{
    public ReactionProfile()
    {
        CreateMap<Reaction, ReactionDto>();
    }
}
