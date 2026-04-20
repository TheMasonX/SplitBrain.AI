using FluentValidation;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Validation;

public sealed class RefactorCodeRequestValidator : AbstractValidator<RefactorCodeRequest>
{
    public RefactorCodeRequestValidator()
    {
        RuleFor(x => x.Goal).NotEmpty();

        RuleFor(x => x.Codebase)
            .NotEmpty()
            .Must(x => x.Count <= 20)
            .WithMessage("Codebase must contain between 1 and 20 files.");

        RuleForEach(x => x.Codebase)
            .SetValidator(new CodeFileValidator());

        RuleFor(x => x.Constraints.MaxFiles)
            .InclusiveBetween(1, 50);
    }
}

public sealed class CodeFileValidator : AbstractValidator<CodeFile>
{
    public CodeFileValidator()
    {
        RuleFor(x => x.Path).NotEmpty();
        RuleFor(x => x.Content).NotEmpty();
    }
}
