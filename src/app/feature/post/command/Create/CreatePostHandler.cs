using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class CreatePostHandler
    : IRequestHandler<CreatePostCommand, Result<PostDto>>
{
    private readonly ICurrentIdentityService _identityService;
    private readonly IR2Service _r2Service;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    private readonly IRabbitMQService _rabbitMQService;
    public CreatePostHandler(
        ICurrentIdentityService identityService,
        IR2Service r2Service,
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper,
        IRabbitMQService rabbitMQService)
    {
        _identityService = identityService;
        _r2Service = r2Service;
        _redisService = redisService;
        _dbContext = dbContext;
        _mapper = mapper;
        _rabbitMQService = rabbitMQService;
    }

    public async Task<Result<PostDto>> Handle(
        CreatePostCommand request,
        CancellationToken cancellationToken)
    {
        var identity = _identityService.GetAccountId();
        if(identity is null)
            return Result.Fail("Unauthorized");

        List<string>? urls = null;
        if(request.images?.Any() == true)
        {
            urls = new List<string>();
            foreach(var image in request.images)
            {
                var url = await _r2Service.UploadAsync(
                    image.stream,
                    image.fileName,
                    cancellationToken);

                urls.Add(url);
            }
        }

        var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

        var shopFilter = Builders<Shop>.Filter.And(
            Builders<Shop>.Filter.Eq(x => x.Name, request.shopName),
            Builders<Shop>.Filter.Eq(x => x.Address, request.shopAddress),
            Builders<Shop>.Filter.Eq(x => x.IsDeleted, false));

        var existingShop = await shopCollection
            .Find(shopFilter)
            .FirstOrDefaultAsync(cancellationToken);

        Guid shopId;
        if(existingShop is not null)
        {
            shopId = existingShop.Id;
        }
        else
        {
            var shopResult = Shop.Create(request.shopName, request.shopAddress);
            if(!shopResult.IsSuccess)
                return Result.Fail(shopResult.Error);

            Shop shop = shopResult.Value;

            await shopCollection.InsertOneAsync(
                shop,
                cancellationToken : cancellationToken);

            var shopSummaryDto = _mapper.Map<ShopSummaryDto>(shop);

            await _redisService.SetAsync(
                CacheKey.Shop(shop.Id),
                shopSummaryDto,
                CacheKey.DefaultShopTTL,
                cancellationToken);

            shopId = shop.Id;
        }

        var postResult = Post.Create(
            identity.Value,
            shopId,
            request.caption,
            request.rate,
            urls);
        if(!postResult.IsSuccess)
            return Result.Fail(postResult.Error);

        Post post = postResult.Value;

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        await postCollection.InsertOneAsync(
            post,
            cancellationToken : cancellationToken);

        var dto = _mapper.Map<PostDto>(post);
        var summaryDto = _mapper.Map<PostSummaryDto>(post);

        string cacheKey = CacheKey.Post(post.Id);

        await _redisService.SetAsync(
            cacheKey,
            summaryDto,
            CacheKey.DefaultPostTTL,
            cancellationToken);

        var recalculateMessage = new RecalculateShopRateMessage(
            Guid.NewGuid(),
            shopId);

        await _rabbitMQService.PublishAsync(
            QueueName.RecalculateShopRate,
            recalculateMessage,
            cancellationToken);

        return Result.Ok(dto);
    }
}
