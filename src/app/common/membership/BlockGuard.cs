using domain;
using MongoDB.Driver;

namespace app;

// Whether a block separates two accounts in either direction, answered from the actor's own
// document with one _id-anchored lookup.
//
// Deliberately not BlockedAccountSet. That set is capped at 300 and cached because it feeds
// the read side, where the cap only degrades what a viewer sees. A cap in a write guard is a
// hole: the account past it could still follow, react and comment. This reads the arrays
// directly, uncapped and current.
public static class BlockGuard
{
    public static Task<bool> IsBlockedAsync(
        IMongoCollection<Account> accounts,
        Guid actorId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        // Both directions in one filter. Checking only BlockedBy would still let an account
        // act on someone it blocked itself, which a stale client or a direct link reaches.
        var filter = Builders<Account>.Filter.And(
            Builders<Account>.Filter.Eq(x => x.Id, actorId),
            Builders<Account>.Filter.Or(
                Builders<Account>.Filter.AnyEq(x => x.Blocks.Select(b => b.AccountId), targetId),
                Builders<Account>.Filter.AnyEq(x => x.BlockedBy, targetId)));

        return accounts.Find(filter).AnyAsync(cancellationToken);
    }
}
