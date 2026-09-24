namespace unit;

public class UnsaveShopValidatorTests
{
    private static readonly UnsaveShopValidator Validator = new();

    [Fact]
    public void EmptyShopId_IsRejected()
    {
        var result = Validator.Validate(new UnsaveShopCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "ShopId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new UnsaveShopCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
