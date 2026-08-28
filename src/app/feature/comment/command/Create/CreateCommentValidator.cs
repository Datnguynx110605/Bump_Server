using FluentValidation;

namespace app;

internal sealed class CreateCommentValidator
    : AbstractValidator<CreateCommentCommand>
{
    public CreateCommentValidator()
    {
        RuleFor(x => x.postId)
            .NotEqual(Guid.Empty).WithMessage("PostId is required");
        RuleFor(x => x.content)
            .MaximumLength(2000).WithMessage("Content is too long, maximum length is 2000 characters");
        RuleForEach(x => x.images).ChildRules(image =>
        {
            image.RuleFor(x => x.stream)
                .NotNull().WithMessage("File is required");
            image.RuleFor(x => x.fileName)
                .NotEmpty().WithMessage("FileName is required");
        });
    }
}
