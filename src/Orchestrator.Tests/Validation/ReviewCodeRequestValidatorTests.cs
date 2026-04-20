using FluentAssertions;
using FluentValidation;
using Orchestrator.Core.Models;
using Orchestrator.Core.Validation;

namespace Orchestrator.Tests.Validation;

public sealed class ReviewCodeRequestValidatorTests
{
    private readonly ReviewCodeRequestValidator _validator = new();

    private static ReviewCodeRequest Valid() => new()
    {
        Code = "public class Foo {}",
        Language = "csharp",
        Focus = "bugs"
    };

    [Fact]
    public void ValidRequest_PassesValidation()
    {
        var result = _validator.Validate(Valid());
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyCode_FailsValidation(string code)
    {
        var request = Valid() with { Code = code };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ReviewCodeRequest.Code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyLanguage_FailsValidation(string language)
    {
        var request = Valid() with { Language = language };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ReviewCodeRequest.Language));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("style")]
    [InlineData("")]
    public void InvalidFocus_FailsValidation(string focus)
    {
        var request = Valid() with { Focus = focus };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ReviewCodeRequest.Focus));
    }

    [Theory]
    [InlineData("architecture")]
    [InlineData("performance")]
    [InlineData("bugs")]
    [InlineData("readability")]
    [InlineData("security")]
    public void ValidFocusValues_PassValidation(string focus)
    {
        var request = Valid() with { Focus = focus };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateOrThrow_ThrowsOnInvalidRequest()
    {
        var request = Valid() with { Code = "" };
        var act = () => request.ValidateOrThrow(_validator);
        act.Should().Throw<ValidationException>();
    }
}
