namespace unit;

public class UnblockAccountValidatorTests
{
    private static readonly UnblockAccountValidator Validator = new();

    [Fact]
    public void EmptyAccountId_IsRejected()
    {
        var result = Validator.Validate(new UnblockAccountCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "AccountId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new UnblockAccountCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
