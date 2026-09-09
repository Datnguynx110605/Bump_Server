using domain;

namespace app;

public static class PostFeedEnricher
{
    public static async Task<PostFeedItemDto> EnrichAsync(
        IMongoDbContext dbContext,
        PostSummaryDto post,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var postCollection = dbContext.GetCollection<Post>(CollectionName.Post);
        var accountCollection = dbContext.GetCollection<Account>(CollectionName.Account);

        var isReacted = await ReactionMembership.HasReactedToPostAsync(postCollection, post.Id, accountId, cancellationToken);
        var isSaved = await SaveMembership.HasSavedPostAsync(accountCollection, accountId, post.Id, cancellationToken);

        return new PostFeedItemDto(post, isReacted, isSaved);
    }

    public static async Task<CursorPageResult<PostFeedItemDto>> EnrichAsync(
        IMongoDbContext dbContext,
        CursorPageResult<PostSummaryDto> page,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var postCollection = dbContext.GetCollection<Post>(CollectionName.Post);
        var accountCollection = dbContext.GetCollection<Account>(CollectionName.Account);

        var ids = page.Items.Select(x => x.Id).ToList();

        // One reads Post, the other Account, and neither needs the other's answer, so they
        // overlap instead of costing two round trips back to back.
        var reactedTask = ReactionMembership.ReactedPostIdsAsync(postCollection, ids, accountId, cancellationToken);
        var savedTask = SaveMembership.SavedPostIdsAsync(accountCollection, accountId, ids, cancellationToken);

        var reactedIds = await reactedTask;
        var savedIds = await savedTask;

        var items = page.Items
            .Select(x => new PostFeedItemDto(x, reactedIds.Contains(x.Id), savedIds.Contains(x.Id)))
            .ToList();

        return new CursorPageResult<PostFeedItemDto>(items, page.NextCursor, page.HasMore);
    }
}
