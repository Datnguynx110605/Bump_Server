namespace unit;

public class SaveShopValidatorTests
{
    private static readonly SaveShopValidator Validator = new();

    [Fact]
    public void EmptyShopId_IsRejected()
    {
        var result = Validator.Validate(new SaveShopCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "ShopId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new SaveShopCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
