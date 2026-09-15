using FluentValidation;

namespace app;

internal sealed class GetAccountByKeywordValidator
    : AbstractValidator<GetAccountByKeywordQuery>
{
    // The keyword reaches a MongoDB regex and, through CacheKey.KeywordSignature, a Redis
    // key. NotEmpty because an empty keyword escapes to a match-everything regex; the
    // length cap because nothing downstream bounded what a caller could send.
    private const int KeywordMaxLength = 100;

    public GetAccountByKeywordValidator()
    {
        RuleFor(x => x.keyword)
            .NotEmpty().WithMessage("Keyword is required")
            .MaximumLength(KeywordMaxLength).WithMessage($"Keyword maximum length is {KeywordMaxLength} characters");
    }
}
