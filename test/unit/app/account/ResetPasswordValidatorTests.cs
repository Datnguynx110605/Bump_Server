namespace unit;

public class ResetPasswordValidatorTests
{
    private static readonly ResetPasswordValidator Validator = new();

    [Fact]
    public void EmptyRegistrationToken_IsRejected()
    {
        var result = Validator.Validate(new ResetPasswordCommand("", "validpassword"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "RegistrationToken is required");
    }

    [Fact]
    public void EmptyNewPassword_IsRejected()
    {
        var result = Validator.Validate(new ResetPasswordCommand("token", ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "New password is required");
    }

    [Fact]
    public void NewPasswordTooLong_IsRejected()
    {
        var result = Validator.Validate(new ResetPasswordCommand("token", new string('a', 41)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "New password is too long, maximum length is 40 characters");
    }

    [Fact]
    public void NewPasswordTooShort_IsRejected()
    {
        var result = Validator.Validate(new ResetPasswordCommand("token", new string('a', 9)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "New password is too short, minimum length is 10 characters");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new ResetPasswordCommand("token", "validpassword"));

        Assert.True(result.IsValid);
    }
}
