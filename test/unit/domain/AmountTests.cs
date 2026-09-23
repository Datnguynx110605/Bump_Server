namespace unit;

public class AmountTests
{
    [Fact]
    public void Negative_IsRejected()
    {
        DomainResult<Amount> result = Amount.Create(-1);

        Assert.False(result.IsSuccess);
        Assert.Equal("Amount must be a non-negative integer", result.Error);
    }

    [Fact]
    public void Zero_IsAccepted()
    {
        DomainResult<Amount> result = Amount.Create(0);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.Value);
    }

    [Fact]
    public void Positive_IsAccepted()
    {
        DomainResult<Amount> result = Amount.Create(5);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.Value);
    }
}
