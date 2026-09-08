using FluentValidation;

namespace app;

internal sealed class CreatePostValidator
    : AbstractValidator<CreatePostCommand>
{
    public CreatePostValidator()
    {
        RuleFor(x => x.shopName)
            .NotEmpty().WithMessage("ShopName is required")
            .MaximumLength(200).WithMessage("ShopName is too long, maximum length is 200 characters");
        RuleFor(x => x.shopAddress)
            .NotEmpty().WithMessage("ShopAddress is required")
            .MaximumLength(200).WithMessage("ShopAddress is too long, maximum length is 200 characters");
        RuleFor(x => x.caption)
            .MaximumLength(2000).WithMessage("Caption is too long, maximum length is 2000 characters");
        RuleFor(x => x.rate.Value)
            .InclusiveBetween(1, 5).WithMessage("Rate must be between 1 and 5");
        RuleForEach(x => x.images).ChildRules(image =>
        {
            image.RuleFor(x => x.stream)
                .NotNull().WithMessage("File is required");
            image.RuleFor(x => x.fileName)
                .NotEmpty().WithMessage("FileName is required");
        });
    }
}
