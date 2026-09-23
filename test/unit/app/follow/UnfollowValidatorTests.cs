namespace unit;

public class UnfollowValidatorTests
{
    private static readonly UnfollowValidator Validator = new();

    [Fact]
    public void EmptyFollowingId_IsRejected()
    {
        var result = Validator.Validate(new UnfollowCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "FollowingId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new UnfollowCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
