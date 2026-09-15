using domain;
using MongoDB.Driver;

namespace app;

public sealed record AccountProjection
{
    public static ProjectionDefinition<Account> Summary = Builders<Account>.Projection
        .Include(x => x.Name)
        .Include(x => x.Avatars)
        .Include(x => x.Bio)
        .Include(x => x.FollowerAmount)
        .Include(x => x.FollowingAmount)
        .Include(x => x.IsTrusted);
}
