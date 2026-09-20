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
        await CreateNotificationIndexAsync(database, cancellationToken);
        await CreateDeviceTokenIndexAsync(database, cancellationToken);
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
                // GetAccountByKeyword matches on the dotted path Name.Value, which the index
                // on the Name subdocument above does not serve. Post and shop already have
                // the equivalent index on their normalized search fields.
                new CreateIndexModel<Account>(
                    keys.Ascending("Name.Value")
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                // Multikey indexes backing the ElemMatch/AnyEq membership lookups in
                // SaveMembership and AccountService, which otherwise scan the
                // whole collection on every feed request.
                new CreateIndexModel<Account>(keys.Ascending("Followers.AccountId")),
                new CreateIndexModel<Account>(keys.Ascending("Followings.AccountId")),
                // Blocks is read by membership checks the same way, and is also one of the
                // arrays ProfileService rewrites on every display-name and avatar change.
                new CreateIndexModel<Account>(keys.Ascending("Blocks.AccountId")),
                new CreateIndexModel<Account>(keys.Ascending("SavePosts.PostId")),
                new CreateIndexModel<Account>(keys.Ascending("SaveShops.ShopId")),
                // Backs the retention sweep in SoftDeletePurgeHost. Partial so it only
                // carries soft-deleted rows instead of the whole collection.
                new CreateIndexModel<Account>(
                    keys.Ascending(x => x.DeletedAt),
                    new CreateIndexOptions<Account>
                    {
                        PartialFilterExpression = Builders<Account>.Filter.Eq(x => x.IsDeleted, true)
                    }),
                // Backs GetRelatedPostHandler's trusted-account fallback tier, which filters
                // IsTrusted and sorts by follower count. Partial so it only carries trusted
                // rows instead of the whole collection.
                new CreateIndexModel<Account>(
                    keys.Descending("FollowerAmount.Value"),
                    new CreateIndexOptions<Account>
                    {
                        PartialFilterExpression = Builders<Account>.Filter.And(
                            Builders<Account>.Filter.Eq(x => x.IsTrusted, true),
                            Builders<Account>.Filter.Eq(x => x.IsDeleted, false))
                    })
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
                    keys.Ascending(x => x.CaptionNormalized)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                // Backs ReactionMembership's per-request membership lookups and the
                // ElemMatch reaction cleanup in AccountService.
                new CreateIndexModel<Post>(keys.Ascending("Reactions.AccountId")),
                // Backs the retention sweep in SoftDeletePurgeHost. Partial so it only
                // carries soft-deleted rows instead of the whole collection.
                new CreateIndexModel<Post>(
                    keys.Ascending(x => x.DeletedAt),
                    new CreateIndexOptions<Post>
                    {
                        PartialFilterExpression = Builders<Post>.Filter.Eq(x => x.IsDeleted, true)
                    })
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
                // Backs the unfiltered admin listing in GetAllCommentHandler, which
                // would otherwise collection-scan — account and post already have
                // the equivalent index for their own all-rows queries.
                new CreateIndexModel<Comment>(
                    keys.Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                // Backs the ElemMatch reaction cleanup in AccountService.
                new CreateIndexModel<Comment>(keys.Ascending("Reactions.AccountId")),
                // Backs the retention sweep in SoftDeletePurgeHost. Partial so it only
                // carries soft-deleted rows instead of the whole collection.
                new CreateIndexModel<Comment>(
                    keys.Ascending(x => x.DeletedAt),
                    new CreateIndexOptions<Comment>
                    {
                        PartialFilterExpression = Builders<Comment>.Filter.Eq(x => x.IsDeleted, true)
                    })
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
                // Backs the retention sweep in SoftDeletePurgeHost. Partial so it only
                // carries soft-deleted rows instead of the whole collection.
                new CreateIndexModel<Shop>(
                    keys.Ascending(x => x.DeletedAt),
                    new CreateIndexOptions<Shop>
                    {
                        PartialFilterExpression = Builders<Shop>.Filter.Eq(x => x.IsDeleted, true)
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
                // Backs the $nearSphere lookup in GetBestShopAroundHandler. A 2dsphere
                // index is sparse over its geo field, so shops with no Location are
                // absent from it and never surface in a proximity search.
                new CreateIndexModel<Shop>(keys.Geo2DSphere(x => x.Location))
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

    // Backs the recipient's notification listing and the per-account delete in
    // AccountService, both of which would otherwise collection-scan.
    private static Task CreateNotificationIndexAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var keys = Builders<Notification>.IndexKeys;

        return database.GetCollection<Notification>(CollectionName.Notification).Indexes.CreateManyAsync(
            new[]
            {
                new CreateIndexModel<Notification>(
                    keys.Ascending(x => x.ToAccountId)
                        .Ascending(x => x.IsDeleted)
                        .Descending(x => x.CreatedAt)
                        .Descending(x => x.Id)),
                // The actor side, which only ProfileService reads — it refreshes the cached
                // avatar on every notification this account raised. Without this the fan-out
                // scans the largest collection in the system on every avatar change.
                new CreateIndexModel<Notification>(keys.Ascending(x => x.ActorAccountId))
            },
            cancellationToken : cancellationToken);
    }

    private static Task CreateDeviceTokenIndexAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var keys = Builders<DeviceToken>.IndexKeys;

        return database.GetCollection<DeviceToken>(CollectionName.DeviceToken).Indexes.CreateManyAsync(
            new[]
            {
                // One row per physical device, and the key RegisterDeviceHandler upserts on.
                // Without the uniqueness, signing a second account in on the same phone would
                // insert a rival row and both accounts would receive each other's pushes.
                new CreateIndexModel<DeviceToken>(
                    keys.Ascending(x => x.Token),
                    new CreateIndexOptions { Unique = true }),
                // Backs the per-account lookup ApnsService runs once per notification.
                new CreateIndexModel<DeviceToken>(
                    keys.Ascending(x => x.AccountId)
                        .Ascending(x => x.IsDeleted)),
                // Backs the retention sweep in SoftDeletePurgeHost, which filters DeletedAt
                // and had no index to do it with — a full collection scan every six hours.
                // Partial for the same reason as the other aggregates: it only needs to carry
                // the retired rows.
                new CreateIndexModel<DeviceToken>(
                    keys.Ascending(x => x.DeletedAt),
                    new CreateIndexOptions<DeviceToken>
                    {
                        PartialFilterExpression = Builders<DeviceToken>.Filter.Eq(x => x.IsDeleted, true)
                    })
            },
            cancellationToken : cancellationToken);
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
                        .Descending(x => x.Id)),
                // Backs the per-account revocation in ChangePassword and ResetPassword and
                // the delete cascade in AccountService, all of which filter on AccountId
                // plus IsRevoked and would otherwise scan the collection.
                new CreateIndexModel<RefreshToken>(
                    keys.Ascending(x => x.AccountId)
                        .Ascending(x => x.IsRevoked)),
                // Backs the revoked-token sweep in SoftDeletePurgeHost.
                new CreateIndexModel<RefreshToken>(
                    keys.Ascending(x => x.IsRevoked),
                    new CreateIndexOptions<RefreshToken>
                    {
                        PartialFilterExpression = Builders<RefreshToken>.Filter.Eq(x => x.IsRevoked, true)
                    })
            },
            cancellationToken);
    }
}
