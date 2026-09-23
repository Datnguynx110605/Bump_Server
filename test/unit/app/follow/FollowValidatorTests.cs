namespace unit;

public class FollowValidatorTests
{
    private static readonly FollowValidator Validator = new();

    [Fact]
    public void EmptyFollowingId_IsRejected()
    {
        var result = Validator.Validate(new FollowCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "FollowingId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new FollowCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
