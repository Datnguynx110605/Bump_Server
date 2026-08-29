using FluentValidation;

namespace app;

internal sealed class UnsaveShopValidator
    : AbstractValidator<UnsaveShopCommand>
{
    public UnsaveShopValidator()
    {
        RuleFor(x => x.shopId)
            .NotEqual(Guid.Empty).WithMessage("ShopId is required");
    }
}
