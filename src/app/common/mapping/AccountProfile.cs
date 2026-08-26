using AutoMapper;
using domain;

namespace app;

public sealed class AccountProfile : Profile
{
    public AccountProfile()
    {
        CreateMap<Account, AccountDto>();
        CreateMap<Account, AccountSummaryDto>()
            .ForCtorParam(nameof(AccountSummaryDto.Avatar), opt => opt.MapFrom(s => s.Avatars.LastOrDefault()));
    }
}
