namespace unit;

public class EmailTests
{
    [Fact]
    public void Empty_IsRejected()
    {
        DomainResult<Email> result = Email.Create(string.Empty);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Whitespace_IsRejected()
    {
        DomainResult<Email> result = Email.Create("   ");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void MissingAtSign_IsRejected()
    {
        DomainResult<Email> result = Email.Create("not-an-email.com");

        Assert.False(result.IsSuccess);
        Assert.Equal("Email is invalid", result.Error);
    }

    [Fact]
    public void OverTwoHundredChars_IsRejected()
    {
        string local = new string('a', 195);
        string value = $"{local}@x.com";
        Assert.True(value.Length > 200);

        DomainResult<Email> result = Email.Create(value);

        Assert.False(result.IsSuccess);
        Assert.Equal("Email is invalid", result.Error);
    }

    [Fact]
    public void ValidEmail_IsAccepted()
    {
        DomainResult<Email> result = Email.Create("user@example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal("user@example.com", result.Value!.Value);
    }
}
