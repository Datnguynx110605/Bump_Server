using FluentValidation;

namespace app;

internal sealed class SaveShopValidator
    : AbstractValidator<SaveShopCommand>
{
    public SaveShopValidator()
    {
        RuleFor(x => x.shopId)
            .NotEqual(Guid.Empty).WithMessage("ShopId is required");
    }
}
