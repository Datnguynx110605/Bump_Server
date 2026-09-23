namespace unit;

public class ChangeBioValidatorTests
{
    private static readonly ChangeBioValidator Validator = new();

    [Fact]
    public void TooLong_IsRejected()
    {
        var result = Validator.Validate(new ChangeBioCommand(new string('a', 2001)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Bio is too long, maximum length is 2000 characters");
    }

    [Fact]
    public void Null_Passes()
    {
        var result = Validator.Validate(new ChangeBioCommand(null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AtLimit_Passes()
    {
        var result = Validator.Validate(new ChangeBioCommand(new string('a', 2000)));

        Assert.True(result.IsValid);
    }
}
