using System.Text.Json;
using System.Text.Json.Serialization;
using domain;

namespace infra;

internal sealed class NameJsonConverter : JsonConverter<Name>
{
    public override Name Read(
        ref Utf8JsonReader reader, 
        Type typeToConvert, 
        JsonSerializerOptions options)
    {
        var value = reader.GetString()!;
        return Name.Create(value).Value!;
    }

    public override void Write(
        Utf8JsonWriter writer, 
        Name value, 
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
