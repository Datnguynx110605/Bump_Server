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
        BsonClassMap.RegisterClassMap<Amount>(map =>
        {
            map.MapProperty(x => x.Value);
            map.MapCreator(new Func<int, Amount>(value => Amount.Create(value).Value!), nameof(Amount.Value));
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

        BsonClassMap.RegisterClassMap<RefreshToken>(map => map.AutoMap());

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
