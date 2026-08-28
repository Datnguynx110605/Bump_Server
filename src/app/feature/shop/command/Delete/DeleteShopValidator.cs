using FluentValidation;

namespace app;

internal sealed class DeleteShopValidator
    : AbstractValidator<DeleteShopCommand>
{
    public DeleteShopValidator()
    {
        RuleFor(x => x.shopId)
            .NotEqual(Guid.Empty).WithMessage("ShopId is required");
    }
}
