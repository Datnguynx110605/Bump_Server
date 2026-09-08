using app;
using domain;
using FluentResults;
using MongoDB.Driver;

namespace infra;

public sealed class CommentService : ICommentService
{
    private readonly IMongoDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly IR2Service _r2Service;
    public CommentService(
        IMongoDbContext dbContext,
        INotificationService notificationService,
        IR2Service r2Service)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
        _r2Service = r2Service;
    }

    public async Task<Result> ProcessCreateCommentAsync(
        Guid accountId,
        Guid commentId,
        CancellationToken cancellationToken)
    {
        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var commentProjection = Builders<Comment>.Projection
            .Include(x => x.PostId);

        var comment = await commentCollection
            .Find(x => x.Id == commentId && !x.IsDeleted)
            .Project<Comment>(commentProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(comment is null)
            return Result.Fail(AppError.NotFound("Comment not found"));

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var postProjection = Builders<Post>.Projection
            .Include(x => x.AccountId);

        var post = await postCollection
            .Find(x => x.Id == comment.PostId && !x.IsDeleted)
            .Project<Post>(postProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(post is null)
            return Result.Fail(AppError.NotFound("Post not found"));

        if(post.AccountId == accountId)
            return Result.Ok();

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var accountProjection = Builders<Account>.Projection
            .Include(x => x.Name)
            .Include(x => x.Avatars);

        var account = await accountCollection
            .Find(x => x.Id == accountId && !x.IsDeleted)
            .Project<Account>(accountProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail(AppError.NotFound("Account not found"));

        return await _notificationService.CreateAsync(
            post.AccountId,
            "New comment",
            $"{account.Name.Value} commented on your post",
            NotificationType.Comment,
            comment.PostId,
            accountId,
            account.Avatars.LastOrDefault(),
            cancellationToken);
    }

    // Only removes the comment's images from R2. The soft delete, the parent post's
    // CommentAmount decrement and the cache invalidations stay with whoever deleted the
    // comment, because a caller that returns before those land would serve stale reads;
    // object storage is the one side effect nobody is waiting on.
    public async Task<Result> ProcessAfterDeleteAsync(
        Guid commentId,
        CancellationToken cancellationToken)
    {
        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var projection = Builders<Comment>.Projection
            .Include(x => x.Images);

        var comment = await commentCollection
            .Find(x => x.Id == commentId && x.IsDeleted)
            .Project<Comment>(projection)
            .FirstOrDefaultAsync(cancellationToken);

        if(comment is null)
            return Result.Fail(AppError.NotFound("Comment not found"));

        foreach(var image in comment.Images)
        {
            await _r2Service.DeleteAsync(
                image.Url,
                cancellationToken);
        }

        return Result.Ok();
    }
}
