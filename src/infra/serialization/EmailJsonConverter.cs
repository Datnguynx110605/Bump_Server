using System.Text.Json;
using System.Text.Json.Serialization;
using domain;

namespace infra;

internal sealed class EmailJsonConverter : JsonConverter<Email>
{
    public override Email Read(
        ref Utf8JsonReader reader, 
        Type typeToConvert, 
        JsonSerializerOptions options)
    {
        var value = reader.GetString()!;
        return Email.Create(value).Value!;
    }

    public override void Write(
        Utf8JsonWriter writer, 
        Email value, 
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
