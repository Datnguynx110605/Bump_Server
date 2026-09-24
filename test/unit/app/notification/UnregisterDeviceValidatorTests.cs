namespace unit;

public class UnregisterDeviceValidatorTests
{
    private static readonly UnregisterDeviceValidator Validator = new();

    [Fact]
    public void EmptyToken_IsRejected()
    {
        var result = Validator.Validate(new UnregisterDeviceCommand(""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Token is required");
    }

    [Theory]
    [InlineData(63)]
    [InlineData(201)]
    public void TokenLengthOutOfBounds_IsRejected(int length)
    {
        var result = Validator.Validate(new UnregisterDeviceCommand(new string('a', length)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Token length is invalid");
    }

    [Fact]
    public void NonHexToken_IsRejected()
    {
        var result = Validator.Validate(new UnregisterDeviceCommand(new string('g', 64)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Token must be hexadecimal");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new UnregisterDeviceCommand(new string('a', 64)));

        Assert.True(result.IsValid);
    }
}
