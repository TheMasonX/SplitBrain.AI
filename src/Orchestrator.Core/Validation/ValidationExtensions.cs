using FluentValidation;

namespace Orchestrator.Core.Validation;

public static class ValidationExtensions
{
    public static void ValidateOrThrow<T>(this T instance, IValidator<T> validator)
    {
        var result = validator.Validate(instance);

        if (!result.IsValid)
            throw new ValidationException(result.Errors);
    }
}
