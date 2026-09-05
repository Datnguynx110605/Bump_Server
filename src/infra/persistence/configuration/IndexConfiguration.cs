using app;
using domain;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace infra;

public static class IndexConfiguration
{
    public static async Task CreateAsync(
        IMongoClient client,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var database = client.GetDatabase(configuration["MongoDB:DatabaseName"]!);

        await CreateAccountIndexAsync(database, cancellationToken);
        await CreatePostIndexAsync(database, cancellationToken);
        await CreateCommentIndexAsync(database, cancellationToken);
        await CreateShopIndexAsync(database, cancellationToken);
        await CreateReportIndexAsync(database, cancellationToken);
        await CreateRefreshTokenIndexAsync(database, cancellationToken);
    }

    private static Task CreateAccountIndexAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var keys = Builders<Account>.IndexKeys;

        return database.GetCollection<Account>(CollectionName.Account).Indexes.CreateManyAsync(
            new[]
            {
                new CreateIndexModel<Account>(
                    keys.Ascending(x => x.Email),
                    new CreateIndexOptions<Account>
                    {
                        Unique = true,
                        PartialFilterExpression = Builders<Account>.Filter.Eq(x => x.IsDeleted, false)
                    }),
                new CreateIndexModel<Account>(
                    keys.Ascending(x => x.Name)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Account>(
                    keys.Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                // Multikey indexes backing the ElemMatch/AnyEq membership lookups in
                // SaveMembership and DeleteAccountService, which otherwise scan the
                // whole collection on every feed request.
                new CreateIndexModel<Account>(keys.Ascending("Followers.AccountId")),
                new CreateIndexModel<Account>(keys.Ascending("Followings.AccountId")),
                new CreateIndexModel<Account>(keys.Ascending("SavePosts.PostId")),
                new CreateIndexModel<Account>(keys.Ascending("SaveShops.ShopId"))
            },
            cancellationToken);
    }

    private static Task CreatePostIndexAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var keys = Builders<Post>.IndexKeys;

        return database.GetCollection<Post>(CollectionName.Post).Indexes.CreateManyAsync(
            new[]
            {
                new CreateIndexModel<Post>(
                    keys.Ascending(x => x.AccountId)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Post>(
                    keys.Ascending(x => x.ShopId)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Post>(
                    keys.Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Post>(
                    keys.Ascending(x => x.IsDeleted)
                        .Descending(x => x.ReactionAmount.Value)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Post>(
                    keys.Ascending(x => x.CaptionNormalized)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                // Backs ReactionMembership's per-request membership lookups and the
                // ElemMatch reaction cleanup in DeleteAccountService.
                new CreateIndexModel<Post>(keys.Ascending("Reactions.AccountId"))
            },
            cancellationToken);
    }

    private static Task CreateCommentIndexAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var keys = Builders<Comment>.IndexKeys;

        return database.GetCollection<Comment>(CollectionName.Comment).Indexes.CreateManyAsync(
            new[]
            {
                new CreateIndexModel<Comment>(
                    keys.Ascending(x => x.PostId)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Comment>(
                    keys.Ascending(x => x.AccountId)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                // Backs the ElemMatch reaction cleanup in DeleteAccountService.
                new CreateIndexModel<Comment>(keys.Ascending("Reactions.AccountId"))
            },
            cancellationToken);
    }

    private static Task CreateShopIndexAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var keys = Builders<Shop>.IndexKeys;

        return database.GetCollection<Shop>(CollectionName.Shop).Indexes.CreateManyAsync(
            new[]
            {
                new CreateIndexModel<Shop>(
                    keys.Ascending(x => x.Name).Ascending(x => x.Address),
                    new CreateIndexOptions<Shop>
                    {
                        Unique = true,
                        PartialFilterExpression = Builders<Shop>.Filter.Eq(x => x.IsDeleted, false)
                    }),
                new CreateIndexModel<Shop>(
                    keys.Ascending(x => x.Name)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Shop>(
                    keys.Ascending(x => x.Address)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Shop>(
                    keys.Ascending(x => x.NameNormalized)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Shop>(
                    keys.Ascending(x => x.AddressNormalized)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Shop>(
                    keys.Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Shop>(
                    keys.Ascending(x => x.IsDeleted)
                        .Descending(x => x.Rate.Value)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id))
            },
            cancellationToken);
    }

    private static Task CreateReportIndexAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var keys = Builders<Report>.IndexKeys;

        return database.GetCollection<Report>(CollectionName.Report).Indexes.CreateManyAsync(
            new[]
            {
                new CreateIndexModel<Report>(
                    keys.Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Report>(
                    keys.Ascending(x => x.PostId)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Report>(
                    keys.Ascending(x => x.CommentId)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Report>(
                    keys.Ascending(x => x.AccountId)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                new CreateIndexModel<Report>(
                    keys.Ascending(x => x.ReporterId)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id))
            },
            cancellationToken);
    }

    private static Task CreateRefreshTokenIndexAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var keys = Builders<RefreshToken>.IndexKeys;

        return database.GetCollection<RefreshToken>(CollectionName.RefreshToken).Indexes.CreateManyAsync(
            new[]
            {
                new CreateIndexModel<RefreshToken>(
                    keys.Ascending(x => x.Token),
                    new CreateIndexOptions { Unique = true }),
                new CreateIndexModel<RefreshToken>(
                    keys.Ascending(x => x.ExpiryDate),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }),
                new CreateIndexModel<RefreshToken>(
                    keys.Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id))
            },
            cancellationToken);
    }
}
