using FluentValidation;

namespace app;

internal sealed class GetBestShopAroundValidator
    : AbstractValidator<GetBestShopAroundQuery>
{
    public GetBestShopAroundValidator()
    {
        RuleFor(x => x.latitude)
            .InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90");
        RuleFor(x => x.longitude)
            .InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180");
    }
}
