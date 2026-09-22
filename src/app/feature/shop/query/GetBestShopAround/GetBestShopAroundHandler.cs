using AutoMapper;
using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;

namespace app;

// Nothing here is precomputed, and deliberately so. The 2dsphere index already answers
// "shops near this point" in a single index scan, and the result depends on a location
// that only arrives with the request — a per-cell precomputed list would have to be
// rebuilt for every neighbourhood and would still drop a five-star shop sitting just
// across a cell boundary. There is no Redis layer either: fifty documents off an index
// cost about what the cache round-trip would.
internal sealed class GetBestShopAroundHandler
    : IRequestHandler<GetBestShopAroundQuery, Result<CursorPageResult<ShopSummaryDto>>>
{
    private const int MinRate = 4;
    private const int Limit = 10;
    private const double MaxDistanceMeters = 5000;

    // $nearSphere orders by distance, so the candidates are the fifty nearest shops that
    // already clear MinRate; sorting those by rate and keeping ten is what turns
    // "nearest" into "best nearby". Widening this trades relevance for reach: a larger
    // pool surfaces better-rated shops further away.
    //
    // It does not bound the work, though — MaxDistanceMeters does. Mongo walks the index
    // outward and discards shops below MinRate after the index, so in a neighbourhood of
    // mostly low-rated shops it keeps walking until the radius runs out. Tune the radius,
    // not this, if the query ever gets slow.
    private const int CandidateLimit = 50;

    private readonly IMongoDbContext _dbContext;
    private readonly IMapper _mapper;
    public GetBestShopAroundHandler(
        IMongoDbContext dbContext,
        IMapper mapper)
    {
        _dbContext = dbContext;
        _mapper = mapper;
    }

    public async Task<Result<CursorPageResult<ShopSummaryDto>>> Handle(
        GetBestShopAroundQuery request,
        CancellationToken cancellationToken)
    {
        var shopCollection = _dbContext.GetCollection<Shop>(CollectionName.Shop);

        // GeoJson.Geographic takes longitude first, matching how Shop.Location is stored.
        var origin = GeoJson.Point(GeoJson.Geographic(request.longitude, request.latitude));

        var filter = Builders<Shop>.Filter.And(
            Builders<Shop>.Filter.Eq(x => x.IsDeleted, false),
            // Verified shops only. This is the one surface that exists to be navigated to,
            // and a shop nobody confirmed is exactly the one whose pin sends a person to
            // the wrong place — or to a shop that was never there.
            Builders<Shop>.Filter.Exists(x => x.PlaceId, true),
            Builders<Shop>.Filter.Gte(x => x.Rate.Value, MinRate),
            Builders<Shop>.Filter.NearSphere(x => x.Location, origin, MaxDistanceMeters));

        var shops = await shopCollection
            .Find(filter)
            .Limit(CandidateLimit)
            .Project<Shop>(ShopProjection.Summary)
            .ToListAsync(cancellationToken);

        // OrderByDescending is stable, so shops sharing a rate keep the nearest-first
        // order $nearSphere returned them in. No explicit .Sort() is possible here:
        // MongoDB rejects one alongside $nearSphere, which imposes its own ordering.
        var items = shops
            .OrderByDescending(x => x.Rate.Value)
            .Take(Limit)
            .Select(x => _mapper.Map<ShopSummaryDto>(x))
            .ToList();

        // A fixed top-ten answer has no next page, matching the shape every other list
        // endpoint returns so the client decodes it the same way.
        return Result.Ok(new CursorPageResult<ShopSummaryDto>(items, null, false));
    }
}
