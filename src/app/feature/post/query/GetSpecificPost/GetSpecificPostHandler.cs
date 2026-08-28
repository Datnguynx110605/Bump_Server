using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetSpecificPostHandler
    : IRequestHandler<GetSpecificPostQuery, Result<PostSummaryDto>>
{
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetSpecificPostHandler(
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<PostSummaryDto>> Handle(
        GetSpecificPostQuery request,
        CancellationToken cancellationToken)
    {
        string cacheKey = CacheKey.Post(request.postId);

        var cachePost = await _redisService.GetAsync<PostSummaryDto>(
            cacheKey,
            cancellationToken
        );

        if(cachePost is not null)
            return Result.Ok(cachePost);

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var dbPost = await postCollection
            .Find(x => x.Id == request.postId && !x.IsDeleted)
            .Project<Post>(PostProjection.Summary)
            .FirstOrDefaultAsync(cancellationToken);

        if(dbPost is null)
            return Result.Fail("Post not found");

        var dto = _mapper.Map<PostSummaryDto>(dbPost);

        await _redisService.SetAsync(
            cacheKey,
            dto,
            CacheKey.DefaultPostTTL,
            cancellationToken);

        return Result.Ok(dto);
    }
}
