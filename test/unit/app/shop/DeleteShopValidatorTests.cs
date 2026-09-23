namespace unit;

public class DeleteShopValidatorTests
{
    private static readonly DeleteShopValidator Validator = new();

    [Fact]
    public void EmptyShopId_IsRejected()
    {
        var result = Validator.Validate(new DeleteShopCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "ShopId is required");
    }

    [Fact]
    public void Valid_Passes()
    {
        var result = Validator.Validate(new DeleteShopCommand(Guid.NewGuid()));

        Assert.True(result.IsValid);
    }
}
