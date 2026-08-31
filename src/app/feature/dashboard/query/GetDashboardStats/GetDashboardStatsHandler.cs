using domain;
using FluentResults;
using MediatR;
using MongoDB.Driver;

namespace app;

internal sealed class GetDashboardStatsHandler
    : IRequestHandler<GetDashboardStatsQuery, Result<DashboardStatsDto>>
{
    private readonly IMongoDbContext _dbContext;
    public GetDashboardStatsHandler(IMongoDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<DashboardStatsDto>> Handle(
        GetDashboardStatsQuery request,
        CancellationToken cancellationToken)
    {
        var accountCollection = _dbContext.GetCollection<Account>(CollectionName.Account);
        var postCollection = _dbContext.GetCollection<Post>(CollectionName.Post);
        var commentCollection = _dbContext.GetCollection<Comment>(CollectionName.Comment);

        var accountCountTask = accountCollection.CountDocumentsAsync(
            Builders<Account>.Filter.Eq(x => x.IsDeleted, false),
            cancellationToken: cancellationToken);

        var postCountTask = postCollection.CountDocumentsAsync(
            Builders<Post>.Filter.Eq(x => x.IsDeleted, false),
            cancellationToken: cancellationToken);

        var commentCountTask = commentCollection.CountDocumentsAsync(
            Builders<Comment>.Filter.Eq(x => x.IsDeleted, false),
            cancellationToken: cancellationToken);

        await Task.WhenAll(
            accountCountTask, 
            postCountTask, 
            commentCountTask);

        var dto = new DashboardStatsDto(
            accountCountTask.Result,
            postCountTask.Result,
            commentCountTask.Result);

        return Result.Ok(dto);
    }
}
