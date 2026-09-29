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
    private readonly IPlaceService _placeService;
    private readonly IR2Service _r2Service;
    private readonly IRedisService _redisService;
    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    private readonly IRabbitMQService _rabbitMQService;
    public CreatePostHandler(
        ICurrentIdentityService identityService,
        IPlaceService placeService,
        IR2Service r2Service,
        IRedisService redisService,
        IMongoDbContext dbContext,
        IMapper mapper,
        IRabbitMQService rabbitMQService)
    {
        _identityService = identityService;
        _placeService = placeService;
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

        var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

        var placeIdFilter = Builders<Shop>.Filter.And(
            Builders<Shop>.Filter.Eq(x => x.PlaceId, request.placeId),
            Builders<Shop>.Filter.Eq(x => x.IsDeleted, false));

        // Projected down to the identifier Mongo assigns, because that is the only thing an
        // existing row is read for. Everything else about the shop is already settled: it was
        // written from the provider's answer the first time somebody posted about this place,
        // and reusing the row never rewrites it.
        Task<Shop> FindShopAsync() =>
            shopCollection
                .Find(placeIdFilter)
                .Project<Shop>(Builders<Shop>.Projection.Include(x => x.PlaceId))
                .FirstOrDefaultAsync(cancellationToken);

        Shop? existingShop = await FindShopAsync();

        // The lookup comes first and the provider call only fires for a place no row holds
        // yet, which is what keeps the common request — a second review of a shop somebody
        // has already posted about — free of any network hop to the provider at all. It
        // works because the identifier is the request's own, echoed back rather than read
        // out of the provider's answer: the lookup never needed the resolve.
        //
        // Still resolved before a single byte is uploaded. Everything below this point has
        // to clean up after the R2 objects on the way out, and a rejected place is much the
        // cheapest failure to answer here, where there is nothing to discard yet.
        PlaceResult? place = null;
        if(existingShop is null)
        {
            place = await _placeService.ResolveAsync(request.placeId, cancellationToken);

            if(place is null)
                return Result.Fail(AppError.BadRequest("Shop could not be verified"));
        }

        List<string>? urls = null;
        if(request.images?.Any() == true)
        {
            // Uploaded together rather than one after another. Up to ten files at a couple
            // of hundred milliseconds each is seconds of wall clock inside a request the
            // P95 budget gives 500ms, and R2 has no reason to be fed serially. Ordering is
            // preserved because Task.WhenAll returns results in the order the tasks were
            // created, which matters: the images array is what the client renders.
            urls = (await Task.WhenAll(request.images.Select(image =>
                _r2Service.UploadAsync(
                    image.stream,
                    image.fileName,
                    cancellationToken)))).ToList();
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

        Guid shopId;
        if(existingShop is not null)
        {
            shopId = existingShop.Id;
        }
        else
        {
            var shopResult = Shop.Create(
                place!.Name,
                place.Address,
                place.Latitude,
                place.Longitude,
                place.PlaceId);
            if(!shopResult.IsSuccess)
            {
                await DiscardUploadsAsync();
                return Result.Fail(shopResult.Error);
            }

            Shop shop = shopResult.Value;
            bool inserted = true;

            try
            {
                await shopCollection.InsertOneAsync(
                    shop,
                    cancellationToken : cancellationToken);
            }
            catch(MongoWriteException ex)
                when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // Two people reviewing the same new shop at the same moment both miss the
                // read above and both insert; the partial unique index on the identifier
                // lets exactly one through. This used to discard the uploads and rethrow, so
                // the loser got a 500 and lost a post that should simply have joined the
                // shop the winner just created. Re-reading is the whole fix — by the time
                // the index has rejected us, the row we wanted is there.
                inserted = false;

                Shop? raced = await FindShopAsync();

                if(raced is null)
                {
                    // The duplicate came from something other than this filter — a soft
                    // deleted row is excluded from the partial index, so this should not
                    // happen — and there is no shop to attach the post to.
                    await DiscardUploadsAsync();
                    return Result.Fail(AppError.Conflict("Shop could not be resolved"));
                }

                shop = raced;
            }

            if(inserted)
            {
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
            }

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
