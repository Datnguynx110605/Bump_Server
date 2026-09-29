namespace unit;

public class CreatePostValidatorTests
{
    private static readonly CreatePostValidator Validator = new();

    private static CreatePostCommand Command(
        string placeId = "place-1",
        string? caption = null,
        int rate = 3,
        List<(Stream stream, string fileName)>? images = null) =>
        new(placeId, caption, Amount.Create(rate).Value!, images);

    [Fact]
    public void EmptyPlaceId_IsRejected()
    {
        var result = Validator.Validate(Command(placeId: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "PlaceId is required");
    }

    [Fact]
    public void PlaceIdOverTwoHundredChars_IsRejected()
    {
        var result = Validator.Validate(Command(placeId: new string('a', 201)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "PlaceId is too long, maximum length is 200 characters");
    }

    [Fact]
    public void CaptionOverTwoThousandChars_IsRejected()
    {
        var result = Validator.Validate(Command(caption: new string('a', 2001)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Caption is too long, maximum length is 2000 characters");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void RateOutOfOneToFive_IsRejected(int rate)
    {
        var result = Validator.Validate(Command(rate: rate));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Rate must be between 1 and 5");
    }

    [Fact]
    public void ImageWithNoFileName_IsRejected()
    {
        var result = Validator.Validate(Command(images: [(new MemoryStream(), "")]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "FileName is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(Command());

        Assert.True(result.IsValid);
    }
}
