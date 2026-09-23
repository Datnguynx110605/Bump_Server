namespace unit;

public class TextNormalizerTests
{
    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TextNormalizer.RemoveDiacriticsAndLower(string.Empty));
    }

    [Fact]
    public void DStroke_FoldsToPlainD()
    {
        Assert.Equal("da nang", TextNormalizer.RemoveDiacriticsAndLower("Đà Nẵng"));
    }

    [Fact]
    public void VietnameseDiacritics_AreFolded()
    {
        Assert.Equal("pho", TextNormalizer.RemoveDiacriticsAndLower("Phở"));
    }

    [Fact]
    public void Result_IsLowercased()
    {
        Assert.Equal("hello", TextNormalizer.RemoveDiacriticsAndLower("HELLO"));
    }
}
