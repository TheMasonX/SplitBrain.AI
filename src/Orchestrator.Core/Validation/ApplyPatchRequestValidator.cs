using FluentValidation;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Validation;

public sealed class ApplyPatchRequestValidator : AbstractValidator<ApplyPatchRequest>
{
    public ApplyPatchRequestValidator()
    {
        RuleFor(x => x.Diff.Files)
            .NotEmpty();

        RuleForEach(x => x.Diff.Files)
            .SetValidator(new DiffFileValidator());
    }
}

public sealed class DiffFileValidator : AbstractValidator<DiffFile>
{
    public DiffFileValidator()
    {
        RuleFor(x => x.Path).NotEmpty();

        RuleFor(x => x.ChangeType)
            .Must(x => x is "modify" or "create" or "delete")
            .WithMessage("ChangeType must be one of: modify, create, delete.");

        RuleFor(x => x.Patch)
            .NotEmpty()
            .When(x => x.ChangeType != "delete");
    }
}
