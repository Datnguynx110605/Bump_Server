using AutoMapper;
using domain;

namespace app;

public sealed class ReportProfile : Profile
{
    public ReportProfile()
    {
        CreateMap<Report, ReportDto>();
    }
}
