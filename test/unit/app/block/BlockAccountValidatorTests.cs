namespace unit;

public class BlockAccountValidatorTests
{
    private static readonly BlockAccountValidator Validator = new();

    [Fact]
    public void EmptyAccountId_IsRejected()
    {
        var result = Validator.Validate(new BlockAccountCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "AccountId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new BlockAccountCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
