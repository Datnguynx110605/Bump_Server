using domain;
using MongoDB.Driver;

namespace app;

public sealed record PostProjection
{
    public static ProjectionDefinition<Post> Summary = Builders<Post>.Projection
        .Include(x => x.AccountId)
        .Include(x => x.ShopId)
        .Include(x => x.Images)
        .Include(x => x.Caption)
        .Include(x => x.Rate)
        .Include(x => x.ReactionAmount)
        .Include(x => x.CommentAmount);
}
