using app;
using domain;
using FluentResults;
using MongoDB.Driver;

namespace infra;

public sealed class CommentService : ICommentService
{
    private readonly IMongoDbContext _dbContext;
    private readonly INotificationService _notificationService;
    public CommentService(
        IMongoDbContext dbContext,
        INotificationService notificationService)
    {
        _dbContext = dbContext;
        _notificationService = notificationService;
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
            return Result.Fail("Comment not found");

        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);

        var postProjection = Builders<Post>.Projection
            .Include(x => x.AccountId);

        var post = await postCollection
            .Find(x => x.Id == comment.PostId && !x.IsDeleted)
            .Project<Post>(postProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(post is null)
            return Result.Fail("Post not found");

        if(post.AccountId == accountId)
            return Result.Ok();

        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);

        var accountProjection = Builders<Account>.Projection
            .Include(x => x.Name);

        var account = await accountCollection
            .Find(x => x.Id == accountId && !x.IsDeleted)
            .Project<Account>(accountProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if(account is null)
            return Result.Fail("Account not found");

        return await _notificationService.CreateAsync(
            post.AccountId,
            "New comment",
            $"{account.Name.Value} commented on your post",
            cancellationToken);
    }
}
