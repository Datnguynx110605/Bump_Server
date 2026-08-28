using System.Text.Json;
using System.Text.Json.Serialization;
using domain;

namespace infra;

internal sealed class AmountJsonConverter : JsonConverter<Amount>
{
    public override Amount Read(
        ref Utf8JsonReader reader, 
        Type typeToConvert, 
        JsonSerializerOptions options)
    {
        var value = reader.GetInt32();
        return Amount.Create(value).Value!;
    }

    public override void Write(
        Utf8JsonWriter writer, 
        Amount value, 
        JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Value);
    }
}
