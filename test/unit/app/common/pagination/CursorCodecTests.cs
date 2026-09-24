using System.Text;

namespace unit;

public class CursorCodecTests
{
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void EncodeDecode_RoundTrips_DateTimeAndGuid(DateTimeKind kind)
    {
        var createdAt = DateTime.SpecifyKind(new DateTime(2024, 3, 15, 10, 30, 0), kind);
        var id = Guid.NewGuid();

        var cursor = CursorCodec.Encode(createdAt, id);
        var decoded = CursorCodec.Decode(cursor);

        Assert.NotNull(decoded);
        Assert.Equal(createdAt, decoded.Value.CreatedAt);
        Assert.Equal(kind, decoded.Value.CreatedAt.Kind);
        Assert.Equal(id, decoded.Value.Id);
    }

    [Fact]
    public void Decode_MalformedBase64_ReturnsNull()
    {
        Assert.Null(CursorCodec.Decode("@@@@"));
    }

    [Fact]
    public void Decode_MissingSeparator_ReturnsNull()
    {
        var cursor = EncodeRaw("no-separator-here");

        Assert.Null(CursorCodec.Decode(cursor));
    }

    [Fact]
    public void Decode_UnparseableDate_ReturnsNull()
    {
        var cursor = EncodeRaw($"not-a-date|{Guid.NewGuid()}");

        Assert.Null(CursorCodec.Decode(cursor));
    }

    private static string EncodeRaw(string raw) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
