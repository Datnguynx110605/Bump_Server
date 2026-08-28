using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetAllRefreshTokenHandler
    : IRequestHandler<GetAllRefreshTokenQuery, Result<CursorPageResult<RefreshTokenDto>>>
{
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetAllRefreshTokenHandler(
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<RefreshTokenDto>>> Handle(
        GetAllRefreshTokenQuery request,
        CancellationToken cancellationToken)
    {
        var refreshTokenCollection = _dbContext.GetCollection<RefreshToken>(CollectionName.RefreshToken);

        var filter = Builders<RefreshToken>.Filter.Eq(x => x.IsDeleted, false);

        var page = await CursorPaginator.PaginateAsync(
            refreshTokenCollection,
            filter,
            projection : null,
            request.pageRequest,
            cancellationToken);

        var items = page.Items
            .Select(x => _mapper.Map<RefreshTokenDto>(x))
            .ToList();

        var dtoPage = new CursorPageResult<RefreshTokenDto>(
            items,
            page.NextCursor,
            page.HasMore);

        return Result.Ok(dtoPage);
    }
}
