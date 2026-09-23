namespace unit;

public class DomainResultTests
{
    [Fact]
    public void Success_SetsIsSuccessTrueAndNoError()
    {
        DomainResult result = DomainResult.Success();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_CarriesTheMessage()
    {
        DomainResult result = DomainResult.Failure("something went wrong");

        Assert.False(result.IsSuccess);
        Assert.Equal("something went wrong", result.Error);
    }

    [Fact]
    public void GenericSuccess_ViaNonGenericEntryPoint_CarriesTheValue()
    {
        DomainResult<int> result = DomainResult.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GenericFailure_ViaNonGenericEntryPoint_LeavesValueDefault()
    {
        DomainResult<string> result = DomainResult.Failure<string>("bad input");

        Assert.False(result.IsSuccess);
        Assert.Equal("bad input", result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public void GenericSuccess_CarriesTheValue()
    {
        DomainResult<int> result = DomainResult<int>.Success(7);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void GenericFailure_LeavesValueDefault()
    {
        DomainResult<int> result = DomainResult<int>.Failure("bad input");

        Assert.False(result.IsSuccess);
        Assert.Equal("bad input", result.Error);
        Assert.Equal(0, result.Value);
    }
}
