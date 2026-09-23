namespace unit;

// NotEmpty/MaximumLength(200) duplicate Email.Create's own checks on the only Email the
// command can carry (unreachable, same reasoning as LoginValidatorTests); EmailAddress()
// is reachable, since Email.Create only requires an "@" somewhere.
public class SendEmailForRegisterValidatorTests
{
    private static readonly SendEmailForRegisterValidator Validator = new();

    [Fact]
    public void PassesEmailCreateButFailsEmailFormat_IsRejected()
    {
        var result = Validator.Validate(new SendEmailForRegisterCommand(Email.Create("not-a-real-address@").Value!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Email is invalid");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new SendEmailForRegisterCommand(Email.Create("user@example.com").Value!));

        Assert.True(result.IsValid);
    }
}
