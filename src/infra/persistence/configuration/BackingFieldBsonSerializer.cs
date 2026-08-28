using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace infra;

internal sealed class BackingFieldBsonSerializer<TEntity>
    : SerializerBase<TEntity>, IBsonDocumentSerializer, IBsonIdProvider
{
    private readonly BsonClassMap _classMap;
    private readonly BsonClassMapSerializer<TEntity> _serializer;

    public BackingFieldBsonSerializer(BsonClassMap classMap)
    {
        _classMap = classMap;
        _serializer = new BsonClassMapSerializer<TEntity>(classMap);
    }

    public override TEntity Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        return _serializer.Deserialize(context, args);
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TEntity value)
    {
        _serializer.Serialize(context, args, value);
    }

    public bool GetDocumentId(object document, out object id, out Type idNominalType, out IIdGenerator idGenerator)
    {
        return _serializer.GetDocumentId(document, out id, out idNominalType, out idGenerator);
    }

    public void SetDocumentId(object document, object id)
    {
        _serializer.SetDocumentId(document, id);
    }

    public bool TryGetMemberSerializationInfo(string memberName, out BsonSerializationInfo serializationInfo)
    {
        if(_serializer.TryGetMemberSerializationInfo(memberName, out serializationInfo))
            return true;

        var memberMap = _classMap.GetMemberMapForElement(memberName);
        if(memberMap is null)
            return false;

        serializationInfo = new BsonSerializationInfo(
            memberMap.ElementName,
            memberMap.GetSerializer(),
            memberMap.MemberType);

        return true;
    }
}
