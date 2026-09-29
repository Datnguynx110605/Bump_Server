using domain;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;

namespace app;

// One shop near the viewer, and how far away it is. The distance is kept rather than
// discarded because the ranking pass wants it, and recovering it later would mean a second
// read of a collection this already walked.
internal sealed record NearbyShop(Guid ShopId, double Meters);

// Resolves "which shops is this viewer near" once per geographic cell rather than once per
// viewer. The answer depends only on where the request came from, so two people standing on
// the same street share both this list and every page of the nearby tier built from it —
// which is what makes the largest slice of the feed nearly free at scale.
internal static class NearbyShopResolver
{
    // ponytail: 10km / 250 shops are both unmeasured. Which one binds depends on real shop
    // density — in a dense district the cap collapses "nearby" to sub-kilometre, and in a
    // sparse one the radius starves the tier's quota. Measure with a $nearSphere count at a
    // busy and a quiet origin before moving either.
    private const double MaxDistanceMeters = 10_000;
    private const int MaxShops = 250;

    // ~1.1km at the equator. Coarser means better cache sharing and fewer cursor resets when
    // the viewer moves, at the cost of a blurrier idea of "near me".
    private const double CellsPerDegree = 100;
    private const long MicroDegreesPerCell = 10_000;

    private const double EarthRadiusMeters = 6_371_000;

    // Scaled integers, in the same micro-degree units the cursor carries its origin in, so
    // the cell can be compared and round-tripped without ever being formatted as a decimal.
    public static (long Lat, long Lng) Cell(double latitude, double longitude) =>
        ((long)Math.Round(latitude * CellsPerDegree) * MicroDegreesPerCell,
         (long)Math.Round(longitude * CellsPerDegree) * MicroDegreesPerCell);

    public static async Task<List<NearbyShop>> ResolveAsync(
        IRedisService redisService,
        IMongoDbContext dbContext,
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        var (cellLat, cellLng) = Cell(latitude, longitude);

        string cacheKey = CacheKey.NearbyShops(cellLat, cellLng);

        var cached = await redisService.GetAsync<List<NearbyShop>>(cacheKey, cancellationToken);
        if(cached is not null)
            return cached;

        var shopCollection = dbContext.GetCollection<Shop>(CollectionName.Shop);

        // Resolved from the cell's centre rather than the caller's exact position, so every
        // viewer in the cell computes the same list and therefore the same cache entry. A
        // per-position lookup would be marginally more accurate and would never share.
        double cellCentreLat = cellLat / (double)(MicroDegreesPerCell * CellsPerDegree);
        double cellCentreLng = cellLng / (double)(MicroDegreesPerCell * CellsPerDegree);

        // GeoJson.Geographic takes longitude first, matching how Shop.Location is stored.
        var origin = GeoJson.Point(GeoJson.Geographic(cellCentreLng, cellCentreLat));

        var filter = Builders<Shop>.Filter.And(
            Builders<Shop>.Filter.Eq(x => x.IsDeleted, false),
            Builders<Shop>.Filter.NearSphere(x => x.Location, origin, MaxDistanceMeters));

        var shops = await shopCollection
            .Find(filter)
            .Limit(MaxShops)
            .Project<Shop>(Builders<Shop>.Projection.Include(x => x.Location))
            .ToListAsync(cancellationToken);

        // $nearSphere returns the shops in distance order but not the distances themselves.
        // Getting those from Mongo would mean a $geoNear aggregation, whose stage must come
        // first in the pipeline and whose shape is unlike every other read in the codebase;
        // projecting the coordinate and doing the arithmetic here costs a dozen lines and
        // keeps one query shape.
        var nearby = shops
            .Select(x => new NearbyShop(
                x.Id,
                DistanceMeters(cellCentreLat, cellCentreLng, x.Location[1], x.Location[0])))
            .ToList();

        await redisService.SetAsync(
            cacheKey,
            nearby,
            CacheKey.DefaultNearbyShopsTTL,
            cancellationToken);

        return nearby;
    }

    // Shop.Location is stored [longitude, latitude], the order the 2dsphere index expects and
    // the reverse of how the pair is spoken about — hence the indices at the call site.
    private static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
    {
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLng = (lng2 - lng1) * Math.PI / 180;

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
            * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

        return EarthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
