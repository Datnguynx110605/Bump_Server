using System.Globalization;
using System.Text;

namespace domain;

public static class TextNormalizer
{
    public static string RemoveDiacriticsAndLower(string value)
    {
        if(string.IsNullOrEmpty(value))
            return string.Empty;

        string withoutStroke = value.Replace('đ', 'd').Replace('Đ', 'D');
        string decomposed = withoutStroke.Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);
        foreach(char c in decomposed)
        {
            if(CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .ToLowerInvariant();
    }
}
