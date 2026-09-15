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
            return Result.Fail(AppError.Unauthorized("Unauthorized"));

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

        // The images are in R2 before anything else can fail, and nothing references them
        // until the post row exists — so every exit between here and that insert has to
        // remove them. Previously only the MongoWriteException paths did, which left a
        // failed Shop.Create or Post.Create orphaning the upload with no way to find it
        // again: the retention sweep is Mongo-only and never looks at the bucket.
        // CancellationToken.None because the cleanup must finish even when the request that
        // triggered it has been cancelled.
        async Task DiscardUploadsAsync()
        {
            if(urls is null)
                return;

            foreach(var uploaded in urls)
                await _r2Service.DeleteAsync(uploaded, CancellationToken.None);
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
            // The first post to name a (shop, address) pair fixes its coordinate for
            // good — later posts reuse the row and never overwrite Location. The client
            // sources both from one place-picker result, so a second poster picking the
            // same place submits the same coordinate anyway.
            shopId = existingShop.Id;
        }
        else
        {
            var shopResult = Shop.Create(
                request.shopName,
                request.shopAddress,
                request.latitude,
                request.longitude);
            if(!shopResult.IsSuccess)
            {
                await DiscardUploadsAsync();
                return Result.Fail(shopResult.Error);
            }

            Shop shop = shopResult.Value;

            try
            {
                await shopCollection.InsertOneAsync(
                    shop,
                    cancellationToken : cancellationToken);
            }
            catch(MongoWriteException)
            {
                await DiscardUploadsAsync();
                throw;
            }

            var shopSummaryDto = _mapper.Map<ShopSummaryDto>(shop);

            await Task.WhenAll(
                _redisService.SetAsync(
                    CacheKey.Shop(shop.Id),
                    shopSummaryDto,
                    CacheKey.DefaultShopTTL,
                    cancellationToken),
                ListCache.InvalidateAsync(
                    _redisService,
                    CollectionName.Shop,
                    filterSignature: null,
                    cancellationToken));

            shopId = shop.Id;
        }

        var postResult = Post.Create(
            identity.Value,
            shopId,
            request.caption,
            request.rate,
            urls);
        if(!postResult.IsSuccess)
        {
            await DiscardUploadsAsync();
            return Result.Fail(postResult.Error);
        }

        Post post = postResult.Value;

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        try
        {
            await postCollection.InsertOneAsync(
                post,
                cancellationToken : cancellationToken);
        }
        catch(MongoWriteException)
        {
            await DiscardUploadsAsync();
            throw;
        }

        var dto = _mapper.Map<PostDto>(post);
        var summaryDto = _mapper.Map<PostSummaryDto>(post);

        string cacheKey = CacheKey.Post(post.Id);

        await Task.WhenAll(
            _redisService.SetAsync(
                cacheKey,
                summaryDto,
                CacheKey.DefaultPostTTL,
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                filterSignature: null,
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                $"account:{identity.Value}",
                cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Post,
                $"shop:{shopId}",
                cancellationToken));

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
