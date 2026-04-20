using FluentValidation;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Validation;

public sealed class ReviewCodeRequestValidator : AbstractValidator<ReviewCodeRequest>
{
    private static readonly string[] ValidFocusValues = ["architecture", "performance", "bugs", "readability", "security"];

    public ReviewCodeRequestValidator()
    {
        RuleFor(x => x.Version)
            .NotEmpty();

        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(200_000);

        RuleFor(x => x.Language)
            .NotEmpty();

        RuleFor(x => x.Focus)
            .Must(x => ValidFocusValues.Contains(x))
            .WithMessage($"Focus must be one of: {string.Join(", ", ValidFocusValues)}");

        RuleForEach(x => x.Context)
            .SetValidator(new RelatedFileValidator())
            .When(x => x.Context != null);
    }
}

public sealed class RelatedFileValidator : AbstractValidator<RelatedFile>
{
    public RelatedFileValidator()
    {
        RuleFor(x => x.Path).NotEmpty();
        RuleFor(x => x.Content).NotEmpty();
    }
}
