namespace unit;

public class ChangeAvatarValidatorTests
{
    private static readonly ChangeAvatarValidator Validator = new();

    [Fact]
    public void NullStream_IsRejected()
    {
        var result = Validator.Validate(new ChangeAvatarCommand(null!, "a.png"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "File is required");
    }

    [Fact]
    public void EmptyFileName_IsRejected()
    {
        var result = Validator.Validate(new ChangeAvatarCommand(new MemoryStream(), ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "FileName is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new ChangeAvatarCommand(new MemoryStream(), "a.png"));

        Assert.True(result.IsValid);
    }
}
