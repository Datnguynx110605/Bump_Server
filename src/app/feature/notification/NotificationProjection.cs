using domain;
using MongoDB.Driver;

namespace app;

public sealed record NotificationProjection
{
    public static ProjectionDefinition<Notification> Summary = Builders<Notification>.Projection
        .Include(x => x.ToAccountId)
        .Include(x => x.Title)
        .Include(x => x.Content)
        .Include(x => x.IsSent);
}
