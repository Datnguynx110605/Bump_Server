using domain;
using FluentResults;
using MediatR;
using MongoDB.Bson;
using MongoDB.Driver;

namespace app;

// Creation counts per hour, read straight from CreatedAt. GetDashboardStatsQuery
// answers "how many exist now"; this answers "how fast are they arriving", which a
// running total cannot show and which no sampling loop can reconstruct for the past.
internal sealed class GetDashboardActivityHandler
    : IRequestHandler<GetDashboardActivityQuery, Result<DashboardActivityDto>>
{
    private const int MinWindowHours = 1;
    private const int MaxWindowHours = 720;

    private readonly IMongoDbContext _dbContext;
    public GetDashboardActivityHandler(IMongoDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<DashboardActivityDto>> Handle(
        GetDashboardActivityQuery request,
        CancellationToken cancellationToken)
    {
        if(request.windowHours < MinWindowHours || request.windowHours > MaxWindowHours)
            return Result.Fail(AppError.BadRequest($"WindowHours must be between {MinWindowHours} and {MaxWindowHours}"));

        DateTime since = DateTime.UtcNow.AddHours(-request.windowHours);

        var accountTask = CountByHourAsync<Account>(CollectionName.Account, since, cancellationToken);
        var postTask = CountByHourAsync<Post>(CollectionName.Post, since, cancellationToken);
        var commentTask = CountByHourAsync<Comment>(CollectionName.Comment, since, cancellationToken);

        await Task.WhenAll(
            accountTask,
            postTask,
            commentTask);

        var buckets = accountTask.Result.Keys
            .Union(postTask.Result.Keys)
            .Union(commentTask.Result.Keys)
            .OrderBy(x => x)
            .Select(bucket => new DashboardActivityPointDto(
                bucket,
                accountTask.Result.GetValueOrDefault(bucket),
                postTask.Result.GetValueOrDefault(bucket),
                commentTask.Result.GetValueOrDefault(bucket)))
            .ToList();

        return Result.Ok(new DashboardActivityDto(request.windowHours, buckets));
    }

    private async Task<Dictionary<DateTime, long>> CountByHourAsync<TEntity>(
        string collectionName,
        DateTime since,
        CancellationToken cancellationToken)
        where TEntity : BaseEntity
    {
        var collection = _dbContext.GetCollection<TEntity>(collectionName);

        var filter = Builders<TEntity>.Filter.And(
            Builders<TEntity>.Filter.Eq(x => x.IsDeleted, false),
            Builders<TEntity>.Filter.Gte(x => x.CreatedAt, since));

        // $dateTrunc keeps the bucketing in the server rather than pulling every
        // document back to count them in process.
        var group = new BsonDocument
        {
            { "_id", new BsonDocument("$dateTrunc", new BsonDocument
                {
                    { "date", "$CreatedAt" },
                    { "unit", "hour" }
                })
            },
            { "count", new BsonDocument("$sum", 1) }
        };

        var results = await collection
            .Aggregate()
            .Match(filter)
            .AppendStage<BsonDocument>(new BsonDocument("$group", group))
            .ToListAsync(cancellationToken);

        return results.ToDictionary(
            x => x["_id"].ToUniversalTime(),
            x => (long)x["count"].ToInt64());
    }
}
