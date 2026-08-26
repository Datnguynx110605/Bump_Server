using AutoMapper;
using domain;

namespace app;

public sealed class CommentProfile : Profile
{
    public CommentProfile()
    {
        CreateMap<Comment, CommentDto>();
        CreateMap<Comment, CommentSummaryDto>();
    }
}
