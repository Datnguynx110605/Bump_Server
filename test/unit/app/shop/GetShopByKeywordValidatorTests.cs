namespace unit;

public class GetShopByKeywordValidatorTests
{
    private static readonly GetShopByKeywordValidator Validator = new();
    private static readonly CursorPageRequest Page = new(null, 20);

    [Fact]
    public void EmptyKeyword_IsRejected()
    {
        var result = Validator.Validate(new GetShopByKeywordQuery("", Page));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Keyword is required");
    }

    [Fact]
    public void KeywordTooLong_IsRejected()
    {
        var result = Validator.Validate(new GetShopByKeywordQuery(new string('a', 101), Page));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Keyword maximum length is 100 characters");
    }

    [Fact]
    public void AtLimit_Passes()
    {
        var result = Validator.Validate(new GetShopByKeywordQuery(new string('a', 100), Page));

        Assert.True(result.IsValid);
    }
}
