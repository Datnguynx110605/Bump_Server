using domain;
using MongoDB.Driver;

namespace app;

public sealed record CommentProjection
{
    public static ProjectionDefinition<Comment> Summary = Builders<Comment>.Projection
        .Include(x => x.AccountId)
        .Include(x => x.Content)
        .Include(x => x.Images)
        .Include(x => x.ReactionAmount)
        .Include(x => x.CreatedAt);
}
