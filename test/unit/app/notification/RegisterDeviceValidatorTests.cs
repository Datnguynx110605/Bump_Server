namespace unit;

public class RegisterDeviceValidatorTests
{
    private static readonly RegisterDeviceValidator Validator = new();

    [Fact]
    public void EmptyToken_IsRejected()
    {
        var result = Validator.Validate(new RegisterDeviceCommand("", false));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Token is required");
    }

    [Theory]
    [InlineData(63)]
    [InlineData(201)]
    public void TokenLengthOutOfBounds_IsRejected(int length)
    {
        var result = Validator.Validate(new RegisterDeviceCommand(new string('a', length), false));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Token length is invalid");
    }

    [Fact]
    public void NonHexToken_IsRejected()
    {
        var result = Validator.Validate(new RegisterDeviceCommand(new string('g', 64), false));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Token must be hexadecimal");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new RegisterDeviceCommand(new string('a', 64), false));

        Assert.True(result.IsValid);
    }
}
