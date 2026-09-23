namespace unit;

public class SendEmailForVerifyValidatorTests
{
    private static readonly SendEmailForVerifyValidator Validator = new();

    [Fact]
    public void PassesEmailCreateButFailsEmailFormat_IsRejected()
    {
        var result = Validator.Validate(new SendEmailForVerifyCommand(Email.Create("not-a-real-address@").Value!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Email is invalid");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new SendEmailForVerifyCommand(Email.Create("user@example.com").Value!));

        Assert.True(result.IsValid);
    }
}
