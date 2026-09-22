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

        // Resolved before a single byte is uploaded. Everything below this point has to
        // clean up after the R2 objects on the way out, and a rejected place is much the
        // cheapest failure to answer here, where there is nothing to discard yet.
        PlaceResult? place = null;
        if(!string.IsNullOrWhiteSpace(request.placeId))
        {
            place = await _placeService.ResolveAsync(request.placeId, cancellationToken);

            if(place is null)
                return Result.Fail(AppError.BadRequest("Shop could not be verified"));
        }

        // A verified shop is described entirely by what the provider answered. The
        // request's own name, address and coordinate are read only when no place was named
        // at all — which is the whole guard: a real identifier can never be paired with
        // invented text, because the text is not looked at.
        string shopName = place?.Name ?? request.shopName!;
        string shopAddress = place?.Address ?? request.shopAddress!;
        double? latitude = place?.Latitude ?? request.latitude;
        double? longitude = place?.Longitude ?? request.longitude;

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

        var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

        var nameAddressFilter = Builders<Shop>.Filter.And(
            Builders<Shop>.Filter.Eq(x => x.Name, shopName),
            Builders<Shop>.Filter.Eq(x => x.Address, shopAddress),
            Builders<Shop>.Filter.Eq(x => x.IsDeleted, false));

        var placeIdFilter = place is null
            ? null
            : Builders<Shop>.Filter.And(
                Builders<Shop>.Filter.Eq(x => x.PlaceId, place.PlaceId),
                Builders<Shop>.Filter.Eq(x => x.IsDeleted, false));

        // Projected: only three fields are read from an existing row, and an unprojected
        // read drags the whole shop document back to learn them. IsDeleted is one of the
        // three because AdoptPlace goes through Update(), which refuses a deleted row —
        // leaving it out would have that check pass on a defaulted false rather than on
        // what the row actually says.
        Task<Shop> FindShopAsync(FilterDefinition<Shop> filter) =>
            shopCollection
                .Find(filter)
                .Project<Shop>(Builders<Shop>.Projection
                    .Include(x => x.Id)
                    .Include(x => x.PlaceId)
                    .Include(x => x.IsDeleted))
                .FirstOrDefaultAsync(cancellationToken);

        // Two queries rather than one `$or`, because the order matters: a row that already
        // carries the identifier has to win. The text arm is a fallback that adopts a shop
        // created before verification existed, and adopting onto a second row while the
        // first one holds the same identifier would collide on the unique index.
        Shop? existingShop = placeIdFilter is null ? null : await FindShopAsync(placeIdFilter);
        existingShop ??= await FindShopAsync(nameAddressFilter);

        Guid shopId;
        if(existingShop is not null)
        {
            shopId = existingShop.Id;

            // Matched on its name and address and carrying no identifier: a shop from
            // before verification, or one somebody typed by hand that the provider has now
            // confirmed. Stamping it is what makes it navigable, and it only ever fills an
            // empty slot — the catalogue upgrades itself as people post, with no migration.
            if(place is not null && existingShop.PlaceId is null && existingShop.AdoptPlace(place.PlaceId).IsSuccess)
                await AdoptPlaceAsync(shopCollection, existingShop, cancellationToken);
        }
        else
        {
            var shopResult = Shop.Create(
                shopName,
                shopAddress,
                latitude,
                longitude,
                place?.PlaceId);
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
                // read above and both insert; the partial unique indexes on the identifier
                // and on (Name, Address) let exactly one through. This used to discard the
                // uploads and rethrow, so the loser got a 500 and lost a post that should
                // simply have joined the shop the winner just created. Re-reading is the
                // whole fix — by the time the index has rejected us, the row we wanted is
                // there.
                inserted = false;

                Shop? raced = placeIdFilter is null ? null : await FindShopAsync(placeIdFilter);
                raced ??= await FindShopAsync(nameAddressFilter);

                if(raced is null)
                {
                    // The duplicate came from something other than these filters — a soft
                    // deleted row is excluded from both partial indexes, so this should not
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

    private async Task AdoptPlaceAsync(
        IMongoCollection<Shop> shopCollection,
        Shop shop,
        CancellationToken cancellationToken)
    {
        try
        {
            await shopCollection.UpdateOneAsync(
                Builders<Shop>.Filter.And(
                    Builders<Shop>.Filter.Eq(x => x.Id, shop.Id),
                    Builders<Shop>.Filter.Exists(x => x.PlaceId, false),
                    Builders<Shop>.Filter.Eq(x => x.IsDeleted, false)),
                Builders<Shop>.Update
                    .Set(x => x.PlaceId, shop.PlaceId)
                    .Set(x => x.UpdatedAt, shop.UpdatedAt),
                cancellationToken : cancellationToken);
        }
        catch(MongoWriteException ex)
            when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Another row already holds this identifier — the provider renamed the place
            // and a second row grew under the new name. The post still belongs to the shop
            // it matched, so the stamp is skipped rather than failing a valid post; the
            // two rows are a duplicate for an administrator to merge.
            return;
        }

        // The cached summary still reads as unverified, and it would for a week.
        await Task.WhenAll(
            _redisService.RemoveAsync(CacheKey.Shop(shop.Id), cancellationToken),
            ListCache.InvalidateAsync(
                _redisService,
                CollectionName.Shop,
                filterSignature: null,
                cancellationToken));
    }
}
