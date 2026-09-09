using AutoMapper;
using domain;

namespace app;

public sealed class BlockProfile : Profile
{
    public BlockProfile()
    {
        CreateMap<Block, BlockDto>();
    }
}
