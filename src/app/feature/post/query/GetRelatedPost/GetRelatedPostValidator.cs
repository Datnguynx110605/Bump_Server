using FluentValidation;

namespace app;

// One of the two query validators in the codebase, for the same reason GetBestShopAround has
// one: a coordinate outside its range is a bad request rather than an empty result.
internal sealed class GetRelatedPostValidator : AbstractValidator<GetRelatedPostQuery>
{
    public GetRelatedPostValidator()
    {
        // Both or neither. One coordinate on its own describes no place, and quietly treating
        // it as "no location" would hide a client bug behind a feed that simply stops being
        // local — the hardest kind of bug to notice, because the endpoint keeps working.
        RuleFor(x => x.longitude)
            .NotNull()
            .When(x => x.latitude is not null)
            .WithMessage("Latitude and longitude must be supplied together");

        RuleFor(x => x.latitude)
            .NotNull()
            .When(x => x.longitude is not null)
            .WithMessage("Latitude and longitude must be supplied together");

        RuleFor(x => x.latitude!.Value)
            .InclusiveBetween(-90, 90)
            .When(x => x.latitude is not null)
            .WithMessage("Latitude must be between -90 and 90");

        RuleFor(x => x.longitude!.Value)
            .InclusiveBetween(-180, 180)
            .When(x => x.longitude is not null)
            .WithMessage("Longitude must be between -180 and 180");
    }
}
