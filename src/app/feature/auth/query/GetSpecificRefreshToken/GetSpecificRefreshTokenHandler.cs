using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetSpecificRefreshTokenHandler
    : IRequestHandler<GetSpecificRefreshTokenQuery, Result<RefreshTokenDto>>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetSpecificRefreshTokenHandler(
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<RefreshTokenDto>> Handle(
        GetSpecificRefreshTokenQuery request,
        CancellationToken cancellationToken)
    {
        var refreshTokenCollection = _dbContext.GetCollection<RefreshToken>(CollectionName.RefreshToken);

        var dbRefreshToken = await refreshTokenCollection
            .Find(x => x.Id == request.refreshTokenId && !x.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if(dbRefreshToken is null)
            return Result.Fail("RefreshToken not found");

        var dto = _mapper.Map<RefreshTokenDto>(dbRefreshToken);

        return Result.Ok(dto);
    }
}
