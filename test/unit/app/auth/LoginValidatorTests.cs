namespace unit;

// The NotEmpty/MaximumLength(200) rules on email.Value duplicate what Email.Create already
// enforces on the only Email the command can carry (same reasoning as
// CreateAccountValidatorTests for Name) — not reachable, not tested. EmailAddress() is
// reachable, though: Email.Create only requires "@" somewhere in the string, which is
// looser than FluentValidation's format check, so an address that clears Email.Create can
// still fail here.
public class LoginValidatorTests
{
    private static readonly LoginValidator Validator = new();

    [Fact]
    public void PassesEmailCreateButFailsEmailFormat_IsRejected()
    {
        var result = Validator.Validate(new LoginCommand(Email.Create("not-a-real-address@").Value!, "password"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Email is invalid");
    }

    [Fact]
    public void EmptyPassword_IsRejected()
    {
        var result = Validator.Validate(new LoginCommand(Email.Create("user@example.com").Value!, ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Password is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new LoginCommand(Email.Create("user@example.com").Value!, "password"));

        Assert.True(result.IsValid);
    }
}
