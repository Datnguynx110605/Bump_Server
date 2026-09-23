namespace unit;

public class ChangePasswordValidatorTests
{
    private static readonly ChangePasswordValidator Validator = new();
    private const string ValidPassword = "validpassword";

    [Fact]
    public void EmptyCurrentPassword_IsRejected()
    {
        var result = Validator.Validate(new ChangePasswordCommand("", ValidPassword));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Current password is required");
    }

    [Fact]
    public void EmptyNewPassword_IsRejected()
    {
        var result = Validator.Validate(new ChangePasswordCommand(ValidPassword, ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "New password is required");
    }

    [Fact]
    public void NewPasswordTooLong_IsRejected()
    {
        var result = Validator.Validate(new ChangePasswordCommand(ValidPassword, new string('a', 41)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "New password is too long, maximum length is 40 characters");
    }

    [Fact]
    public void NewPasswordTooShort_IsRejected()
    {
        var result = Validator.Validate(new ChangePasswordCommand(ValidPassword, new string('a', 9)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "New password is too short, minimum length is 10 characters");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new ChangePasswordCommand(ValidPassword, "brandnewpassword"));

        Assert.True(result.IsValid);
    }
}
