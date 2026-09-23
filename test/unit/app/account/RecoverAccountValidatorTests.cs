namespace unit;

public class RecoverAccountValidatorTests
{
    private static readonly RecoverAccountValidator Validator = new();

    [Fact]
    public void EmptyRegistrationToken_IsRejected()
    {
        var result = Validator.Validate(new RecoverAccountCommand(""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "RegistrationToken is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new RecoverAccountCommand("token"));

        Assert.True(result.IsValid);
    }
}
