using FluentValidation;

namespace app;

internal sealed class CreatePostValidator
    : AbstractValidator<CreatePostCommand>
{
    public CreatePostValidator()
    {
        RuleFor(x => x.placeId)
            .MaximumLength(200).WithMessage("PlaceId is too long, maximum length is 200 characters");
        // Name and address are only the shop's identity on the unverified tier. With a
        // place identifier present the handler reads neither — whatever the provider
        // answers is the shop — so requiring them there would demand text nobody uses.
        When(x => string.IsNullOrWhiteSpace(x.placeId), () =>
        {
            RuleFor(x => x.shopName)
                .NotEmpty().WithMessage("ShopName is required")
                .MaximumLength(200).WithMessage("ShopName is too long, maximum length is 200 characters");
            RuleFor(x => x.shopAddress)
                .NotEmpty().WithMessage("ShopAddress is required")
                .MaximumLength(200).WithMessage("ShopAddress is too long, maximum length is 200 characters");
        });
        // Mirrors the guards in Shop.Create. Duplicated deliberately: validators run
        // before the handler, and the handler uploads every image to R2 before it ever
        // reaches Shop.Create — failing there would orphan the uploaded objects.
        RuleFor(x => x)
            .Must(x => (x.latitude is null) == (x.longitude is null))
            .WithMessage("Latitude and longitude must be provided together");
        RuleFor(x => x.latitude)
            .InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90")
            .When(x => x.latitude is not null);
        RuleFor(x => x.longitude)
            .InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180")
            .When(x => x.longitude is not null);
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
