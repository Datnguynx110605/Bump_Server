namespace unit;

public class AdminDeleteAccountValidatorTests
{
    private static readonly AdminDeleteAccountValidator Validator = new();

    [Fact]
    public void EmptyAccountId_IsRejected()
    {
        var result = Validator.Validate(new AdminDeleteAccountCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "AccountId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new AdminDeleteAccountCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
