using domain;
using MongoDB.Driver;

namespace app;

public sealed record ShopProjection
{
    public static ProjectionDefinition<Shop> Summary = Builders<Shop>.Projection
        .Include(x => x.Name)
        .Include(x => x.Address)
        .Include(x => x.Rate);
}
