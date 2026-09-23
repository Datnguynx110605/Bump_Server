namespace unit;

public class ChangeRateValidatorTests
{
    private static readonly ChangeRateValidator Validator = new();

    [Fact]
    public void EmptyPostId_IsRejected()
    {
        var result = Validator.Validate(new ChangeRateCommand(Guid.Empty, Amount.Create(3).Value!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "PostId is required");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void RateOutOfOneToFive_IsRejected(int rate)
    {
        var result = Validator.Validate(new ChangeRateCommand(Guid.NewGuid(), Amount.Create(rate).Value!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Rate must be between 1 and 5");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new ChangeRateCommand(Guid.NewGuid(), Amount.Create(3).Value!));

        Assert.True(result.IsValid);
    }
}
