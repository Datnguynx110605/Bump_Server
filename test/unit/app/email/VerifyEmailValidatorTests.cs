namespace unit;

public class VerifyEmailValidatorTests
{
    private static readonly VerifyEmailValidator Validator = new();
    private static readonly Email ValidEmail = Email.Create("user@example.com").Value!;

    [Fact]
    public void PassesEmailCreateButFailsEmailFormat_IsRejected()
    {
        var result = Validator.Validate(new VerifyEmailCommand(Email.Create("not-a-real-address@").Value!, "123456"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Email is invalid");
    }

    [Fact]
    public void EmptyOtp_IsRejected()
    {
        var result = Validator.Validate(new VerifyEmailCommand(ValidEmail, ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Otp is required");
    }

    [Fact]
    public void OtpShorterThanSixDigits_IsRejected()
    {
        var result = Validator.Validate(new VerifyEmailCommand(ValidEmail, "12345"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Otp is invalid");
    }

    [Fact]
    public void OtpLongerThanSixDigits_IsRejected()
    {
        var result = Validator.Validate(new VerifyEmailCommand(ValidEmail, "1234567"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Otp is invalid");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new VerifyEmailCommand(ValidEmail, "123456"));

        Assert.True(result.IsValid);
    }
}
