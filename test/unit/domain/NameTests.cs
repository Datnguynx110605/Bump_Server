namespace unit;

public class NameTests
{
    [Fact]
    public void Empty_IsRejected()
    {
        DomainResult<Name> result = Name.Create(string.Empty);

        Assert.False(result.IsSuccess);
        Assert.Equal("Name is required", result.Error);
    }

    [Fact]
    public void OverTwoHundredChars_IsRejected()
    {
        string value = new string('a', 201);

        DomainResult<Name> result = Name.Create(value);

        Assert.False(result.IsSuccess);
        Assert.Equal("Name is invalid", result.Error);
    }

    [Fact]
    public void UppercaseCharacter_IsRejected()
    {
        DomainResult<Name> result = Name.Create("John");

        Assert.False(result.IsSuccess);
        Assert.Equal("Name is invalid", result.Error);
    }

    [Fact]
    public void NonAlphanumericCharacter_IsRejected()
    {
        DomainResult<Name> result = Name.Create("john doe");

        Assert.False(result.IsSuccess);
        Assert.Equal("Name is invalid", result.Error);
    }

    [Fact]
    public void LowercaseAndDigits_AreAccepted()
    {
        DomainResult<Name> result = Name.Create("john123");

        Assert.True(result.IsSuccess);
        Assert.Equal("john123", result.Value!.Value);
    }

    // Pins a surprising fact rather than a rule Create enforces: char.IsLower('à') is true
    // in .NET, so the "lowercase letters and digits" rule does not exclude Vietnamese
    // diacritics the way GetAccountByKeywordHandler's own comment assumes. See
    // test-strategy.md §5 row 16 for the search-side consequence this creates.
    [Fact]
    public void LowercaseDiacritic_IsAccepted()
    {
        DomainResult<Name> result = Name.Create("càphê");

        Assert.True(result.IsSuccess);
        Assert.Equal("càphê", result.Value!.Value);
    }
}
