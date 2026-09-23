namespace unit;

// The four rules on x.name.Value duplicate checks Name.Create already enforces — the
// command only ever carries a Name built through Create, so those branches cannot fail
// here without bypassing the type system. Not tested for the same reason
// test-strategy.md §7 won't let a passing test stand in for a specification: a fabricated
// invalid Name would prove the validator's own dead code, not anything a caller can reach.
public class CreateAccountValidatorTests
{
    private static readonly Name ValidName = Name.Create("johndoe").Value!;
    private static readonly string ValidPassword = new('a', 10);

    private static CreateAccountCommand Command(
        string registrationToken = "token",
        string? password = null,
        string? bio = null) =>
        new(registrationToken, ValidName, password ?? ValidPassword, bio);

    private static readonly CreateAccountValidator Validator = new();

    [Fact]
    public void EmptyRegistrationToken_IsRejected()
    {
        var result = Validator.Validate(Command(registrationToken: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "RegistrationToken is required");
    }

    [Fact]
    public void EmptyPassword_IsRejected()
    {
        var result = Validator.Validate(Command(password: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Password is required");
    }

    [Fact]
    public void PasswordTooLong_IsRejected()
    {
        var result = Validator.Validate(Command(password: new string('a', 41)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Password is too long, maximum length is 40 characters");
    }

    [Fact]
    public void PasswordTooShort_IsRejected()
    {
        var result = Validator.Validate(Command(password: new string('a', 9)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Password is too short, minimum length is 10 characters");
    }

    [Fact]
    public void BioTooLong_IsRejected()
    {
        var result = Validator.Validate(Command(bio: new string('b', 2001)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Bio is too long, maximum length is 2000 characters");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(Command());

        Assert.True(result.IsValid);
    }
}
