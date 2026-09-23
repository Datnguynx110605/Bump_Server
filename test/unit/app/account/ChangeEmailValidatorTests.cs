namespace unit;

public class ChangeEmailValidatorTests
{
    private static readonly ChangeEmailValidator Validator = new();

    [Fact]
    public void EmptyRegistrationToken_IsRejected()
    {
        var result = Validator.Validate(new ChangeEmailCommand("", "password"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "RegistrationToken is required");
    }

    [Fact]
    public void EmptyCurrentPassword_IsRejected()
    {
        var result = Validator.Validate(new ChangeEmailCommand("token", ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Current password is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new ChangeEmailCommand("token", "password"));

        Assert.True(result.IsValid);
    }
}
