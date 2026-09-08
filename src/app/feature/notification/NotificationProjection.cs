using domain;
using MongoDB.Driver;

namespace app;

public sealed record NotificationProjection
{
    // A Mongo inclusion projection returns only the fields listed here, so every field
    // NotificationSummaryDto exposes must appear below. Type, TargetId, ActorAccountId
    // and ActorAvatar were missing, so both notification queries returned them as null:
    // clients could not resolve a notification to a target and had no actor avatar to
    // show, which silently disabled deep-linking from the notification list.
    // CreatedAt is listed for the same reason: CursorPaginator combines it in for cursor
    // paging, but the handlers that project directly have nothing adding it, so leaving it
    // out returned a default timestamp to every single-notification read.
    public static ProjectionDefinition<Notification> Summary = Builders<Notification>.Projection
        .Include(x => x.ToAccountId)
        .Include(x => x.Title)
        .Include(x => x.Content)
        .Include(x => x.IsSent)
        .Include(x => x.Type)
        .Include(x => x.TargetId)
        .Include(x => x.ActorAccountId)
        .Include(x => x.ActorAvatar)
        .Include(x => x.CreatedAt);
}
