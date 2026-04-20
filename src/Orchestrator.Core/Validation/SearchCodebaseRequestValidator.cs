using FluentValidation;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Validation;

public sealed class SearchCodebaseRequestValidator : AbstractValidator<SearchCodebaseRequest>
{
    public SearchCodebaseRequestValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty();

        RuleFor(x => x.TopK)
            .InclusiveBetween(1, 20);
    }
}
