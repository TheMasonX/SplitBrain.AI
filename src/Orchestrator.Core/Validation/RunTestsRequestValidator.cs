using FluentValidation;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Validation;

public sealed class RunTestsRequestValidator : AbstractValidator<RunTestsRequest>
{
    public RunTestsRequestValidator()
    {
        RuleFor(x => x.ProjectPath)
            .NotEmpty();

        RuleFor(x => x.TimeoutSeconds)
            .InclusiveBetween(1, 120);
    }
}
