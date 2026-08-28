using System.Globalization;
using System.Text;

namespace app;

internal static class CursorCodec
{
    public static string Encode(DateTime createdAt, Guid id) =>
        EncodeBase64Url(Encoding.UTF8.GetBytes($"{createdAt:O}|{id}"));

    public static (DateTime CreatedAt, Guid Id)? Decode(string? cursor)
    {
        if(string.IsNullOrEmpty(cursor))
            return null;

        try
        {
            var raw = Encoding.UTF8.GetString(DecodeBase64Url(cursor));
            var parts = raw.Split('|');

            return (
                DateTime.Parse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Guid.Parse(parts[1]));
        }
        catch(FormatException)
        {
            return null;
        }
        catch(IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static string EncodeBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');

        return Convert.FromBase64String(padded);
    }
}
