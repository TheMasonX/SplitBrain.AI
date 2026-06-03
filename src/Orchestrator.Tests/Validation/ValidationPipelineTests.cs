using FluentAssertions;
using NUnit.Framework;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Validation;

namespace Orchestrator.Tests.Validation;

[TestFixture]
public class ValidationPipelineTests
{
    private static TaskContext MakeContext(TaskType type = TaskType.Chat, bool structured = false, int? maxLen = null) =>
        new() { TaskType = type, ExpectsStructuredOutput = structured, MaxLength = maxLen };

    // -------------------------------------------------------------------------
    // ValidationPipeline
    // -------------------------------------------------------------------------

    [Test]
    public async Task Pipeline_WhenAllPass_ReturnsPassed()
    {
        var pipeline = new ValidationPipeline([new LengthBoundsValidator()]);
        var (passed, _) = await pipeline.ValidateAsync("Hello world.", MakeContext());
        passed.Should().BeTrue();
    }

    [Test]
    public async Task Pipeline_WhenOneErrors_ReturnsNotPassed()
    {
        var pipeline = new ValidationPipeline([new LengthBoundsValidator()]);
        var (passed, results) = await pipeline.ValidateAsync("", MakeContext());
        passed.Should().BeFalse();
        results.Should().ContainSingle(r => r.Severity == ValidationSeverity.Error);
    }

    [Test]
    public async Task Pipeline_RunsAllValidators()
    {
        var pipeline = new ValidationPipeline([new LengthBoundsValidator(), new RefusalDetector()]);
        var (_, results) = await pipeline.ValidateAsync("Good output.", MakeContext());
        results.Should().HaveCount(2);
    }

    // -------------------------------------------------------------------------
    // LengthBoundsValidator
    // -------------------------------------------------------------------------

    [Test]
    public async Task LengthBounds_EmptyOutput_ReturnsError()
    {
        var validator = new LengthBoundsValidator();
        var result = await validator.ValidateAsync("", MakeContext());
        result.Severity.Should().Be(ValidationSeverity.Error);
    }

    [Test]
    public async Task LengthBounds_ExceedsMax_ReturnsError()
    {
        var validator = new LengthBoundsValidator();
        var result = await validator.ValidateAsync(new string('x', 100), MakeContext(maxLen: 50));
        result.Severity.Should().Be(ValidationSeverity.Error);
    }

    [Test]
    public async Task LengthBounds_CleanEnding_ReturnsPass()
    {
        var validator = new LengthBoundsValidator();
        var result = await validator.ValidateAsync("All done.", MakeContext());
        result.Severity.Should().Be(ValidationSeverity.Pass);
    }

    [Test]
    public async Task LengthBounds_TruncatedMidSentence_ReturnsWarning()
    {
        var validator = new LengthBoundsValidator();
        // No terminal punctuation, long enough to trigger heuristic
        var result = await validator.ValidateAsync("This sentence was cut off in the middle of", MakeContext());
        result.Severity.Should().Be(ValidationSeverity.Warning);
    }

    // -------------------------------------------------------------------------
    // RefusalDetector
    // -------------------------------------------------------------------------

    [Test]
    public async Task RefusalDetector_NoRefusal_ReturnsPass()
    {
        var validator = new RefusalDetector();
        var result = await validator.ValidateAsync("Here is the refactored code.", MakeContext());
        result.Severity.Should().Be(ValidationSeverity.Pass);
    }

    [Test]
    public async Task RefusalDetector_ContainsICannotPhrase_ReturnsError()
    {
        var validator = new RefusalDetector();
        var result = await validator.ValidateAsync("I cannot help with that request.", MakeContext());
        result.Severity.Should().Be(ValidationSeverity.Error);
    }

    [Test]
    public async Task RefusalDetector_ContainsAsAnAiPhrase_ReturnsError()
    {
        var validator = new RefusalDetector();
        var result = await validator.ValidateAsync("As an AI, I am not designed to do this.", MakeContext());
        result.Severity.Should().Be(ValidationSeverity.Error);
    }

    // -------------------------------------------------------------------------
    // StructuredOutputValidator
    // -------------------------------------------------------------------------

    [Test]
    public async Task StructuredOutput_NotExpected_ReturnsPass()
    {
        var validator = new StructuredOutputValidator();
        var result = await validator.ValidateAsync("plain text", MakeContext(structured: false));
        result.Severity.Should().Be(ValidationSeverity.Pass);
    }

    [Test]
    public async Task StructuredOutput_ValidJson_ReturnsPass()
    {
        var validator = new StructuredOutputValidator();
        var result = await validator.ValidateAsync("""{"key":"value"}""", MakeContext(structured: true));
        result.Severity.Should().Be(ValidationSeverity.Pass);
    }

    [Test]
    public async Task StructuredOutput_InvalidJson_ReturnsError()
    {
        var validator = new StructuredOutputValidator();
        var result = await validator.ValidateAsync("{not valid json", MakeContext(structured: true));
        result.Severity.Should().Be(ValidationSeverity.Error);
    }

    [Test]
    public async Task StructuredOutput_NoJsonFound_ReturnsError()
    {
        var validator = new StructuredOutputValidator();
        var result = await validator.ValidateAsync("no braces here", MakeContext(structured: true));
        result.Severity.Should().Be(ValidationSeverity.Error);
    }

    // -------------------------------------------------------------------------
    // CodeSyntaxValidator
    // -------------------------------------------------------------------------

    [Test]
    public async Task CodeSyntaxValidator_BalancedBraces_ReturnsPass()
    {
        var validator = new CodeSyntaxValidator();
        var result = await validator.ValidateAsync("void Foo() { int x = 1; }", MakeContext(TaskType.Refactor));
        result.Severity.Should().Be(ValidationSeverity.Pass);
    }

    [Test]
    public async Task CodeSyntaxValidator_UnbalancedBraces_ReturnsError()
    {
        var validator = new CodeSyntaxValidator();
        var result = await validator.ValidateAsync("void Foo() { int x = 1;", MakeContext(TaskType.Refactor));
        result.Severity.Should().Be(ValidationSeverity.Error);
    }

    [Test]
    public async Task CodeSyntaxValidator_NonCodeTask_ReturnsPass()
    {
        var validator = new CodeSyntaxValidator();
        // Chat tasks shouldn't be syntax-checked
        var result = await validator.ValidateAsync("no brackets here", MakeContext(TaskType.Chat));
        result.Severity.Should().Be(ValidationSeverity.Pass);
    }
}
