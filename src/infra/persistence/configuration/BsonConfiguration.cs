using domain;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace infra;

public static class BsonConfiguration
{
    public static void Register()
    {
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

        RegisterValueObject();
        RegisterEntity();
    }

    private static void RegisterValueObject()
    {
        // Clamped, not null-forgiving. Amount.Create rejects a negative, DomainResult<T>
        // .Failure leaves Value as default — which for a reference type is null — and the
        // `!` only silenced the compiler. A counter that drifted below zero therefore
        // deserialized to a null Amount sitting on a non-nullable property: it reached the
        // client as `null` on a field the app declares required, and threw
        // NullReferenceException at the two places that dereference it in C#
        // (FollowService.TryGrantTrustAsync and GetBestShopAroundHandler's in-memory sort).
        // Reading a drifted row as zero is wrong by the amount of the drift and visible;
        // reading it as null is a crash with no indication of the cause.
        BsonClassMap.RegisterClassMap<Amount>(map =>
        {
            map.MapProperty(x => x.Value);
            map.MapCreator(
                new Func<int, Amount>(value => Amount.Create(value).Value ?? Amount.Create(0).Value!),
                nameof(Amount.Value));
        });

        BsonClassMap.RegisterClassMap<Email>(map =>
        {
            map.MapProperty(x => x.Value);
            map.MapCreator(new Func<string, Email>(value => Email.Create(value).Value!), nameof(Email.Value));
        });

        BsonClassMap.RegisterClassMap<Name>(map =>
        {
            map.MapProperty(x => x.Value);
            map.MapCreator(new Func<string, Name>(value => Name.Create(value).Value!), nameof(Name.Value));
        });
    }

    private static void RegisterEntity()
    {
        BsonClassMap.RegisterClassMap<BaseEntity>(map =>
        {
            map.AutoMap();
            map.MapIdMember(x => x.Id);
            map.SetIgnoreExtraElements(true);
            map.SetIgnoreExtraElementsIsInherited(true);
        });

        BsonClassMap.RegisterClassMap<Account>(map =>
        {
            map.AutoMap();
            map.MapField("_avatars").SetElementName(nameof(Account.Avatars));
            map.MapField("_followers").SetElementName(nameof(Account.Followers));
            map.MapField("_followings").SetElementName(nameof(Account.Followings));
            map.MapField("_blocks").SetElementName(nameof(Account.Blocks));
            map.MapField("_blockedBy").SetElementName(nameof(Account.BlockedBy));
            map.MapField("_savePosts").SetElementName(nameof(Account.SavePosts));
            map.MapField("_saveShops").SetElementName(nameof(Account.SaveShops));
        });

        BsonClassMap.RegisterClassMap<Comment>(map =>
        {
            map.AutoMap();
            map.MapField("_images").SetElementName(nameof(Comment.Images));
            map.MapField("_reactions").SetElementName(nameof(Comment.Reactions));
        });

        BsonClassMap.RegisterClassMap<Post>(map =>
        {
            map.AutoMap();
            map.MapField("_images").SetElementName(nameof(Post.Images));
            map.MapField("_reactions").SetElementName(nameof(Post.Reactions));
        });

        BsonClassMap.RegisterClassMap<Notification>(map => map.AutoMap());

        BsonClassMap.RegisterClassMap<RefreshToken>(map => map.AutoMap());

        BsonClassMap.RegisterClassMap<DeviceToken>(map => map.AutoMap());

        BsonClassMap.RegisterClassMap<Report>(map => map.AutoMap());

        BsonClassMap.RegisterClassMap<Shop>(map => map.AutoMap());

        RegisterBackingFieldSerializer<Account>();
        RegisterBackingFieldSerializer<Comment>();
        RegisterBackingFieldSerializer<Post>();
    }

    private static void RegisterBackingFieldSerializer<TEntity>()
    {
        BsonSerializer.RegisterSerializer(
            new BackingFieldBsonSerializer<TEntity>(BsonClassMap.LookupClassMap(typeof(TEntity))));
    }
}
