using System.Text.Json;

namespace infra;

internal static class DomainJsonOptions
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        Converters =
        {
            new NameJsonConverter(),
            new EmailJsonConverter(),
            new AmountJsonConverter()
        }
    };
}
