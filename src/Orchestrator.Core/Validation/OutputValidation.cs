using System.Runtime.CompilerServices;
using Orchestrator.Core.Enums;

namespace Orchestrator.Core.Validation;

// ---------------------------------------------------------------------------
// Core types
// ---------------------------------------------------------------------------

public enum ValidationSeverity { Pass, Warning, Error }

public record ValidationResult
{
    public required ValidationSeverity Severity { get; init; }
    public required string ValidatorName { get; init; }
    public required string Message { get; init; }
}

public record TaskContext
{
    public required TaskType TaskType { get; init; }
    public bool ExpectsStructuredOutput { get; init; }
    public int? MaxLength { get; init; }
}

/// <summary>
/// Runs after every inference response. Error severity triggers fallback
/// to the next model in the chain.
/// </summary>
public interface IOutputValidator
{
    string Name { get; }
    Task<ValidationResult> ValidateAsync(string output, TaskContext context, CancellationToken ct = default);
}

// ---------------------------------------------------------------------------
// Validation pipeline
// ---------------------------------------------------------------------------

/// <summary>
/// Runs all registered validators in order. Returns all results;
/// callers check Passed to decide whether to trigger fallback.
/// </summary>
public sealed class ValidationPipeline
{
    private readonly IReadOnlyList<IOutputValidator> _validators;

    public ValidationPipeline(IEnumerable<IOutputValidator> validators) =>
        _validators = validators.ToList();

    public async Task<(bool Passed, IReadOnlyList<ValidationResult> Results)> ValidateAsync(
        string output, TaskContext context, CancellationToken ct = default)
    {
        var results = new List<ValidationResult>(_validators.Count);
        foreach (var validator in _validators)
            results.Add(await validator.ValidateAsync(output, context, ct));

        var passed = results.All(r => r.Severity != ValidationSeverity.Error);
        return (passed, results);
    }
}

// ---------------------------------------------------------------------------
// Validator: LengthBounds
// ---------------------------------------------------------------------------

/// <summary>
/// Error if output is empty; Warning if abruptly truncated; Error if over max.
/// </summary>
public sealed class LengthBoundsValidator : IOutputValidator
{
    public string Name => "LengthBounds";

    public Task<ValidationResult> ValidateAsync(string output, TaskContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(output))
            return Task.FromResult(Error("Output is empty."));

        if (context.MaxLength.HasValue && output.Length > context.MaxLength.Value)
            return Task.FromResult(Error($"Output length {output.Length} exceeds maximum {context.MaxLength.Value}."));

        // Heuristic: abrupt truncation mid-sentence
        var trimmed = output.TrimEnd();
        if (trimmed.Length > 20 && !EndsCleanly(trimmed))
            return Task.FromResult(Warn("Output appears to be truncated mid-sentence."));

        return Task.FromResult(Pass());
    }

    private static bool EndsCleanly(string s)
    {
        var last = s[^1];
        return last is '.' or '!' or '?' or '\n' or '}' or ')' or ';' or '`' or '"' or '\'';
    }

    private ValidationResult Pass() => new() { Severity = ValidationSeverity.Pass, ValidatorName = Name, Message = "OK" };
    private ValidationResult Warn(string msg) => new() { Severity = ValidationSeverity.Warning, ValidatorName = Name, Message = msg };
    private ValidationResult Error(string msg) => new() { Severity = ValidationSeverity.Error, ValidatorName = Name, Message = msg };
}

// ---------------------------------------------------------------------------
// Validator: RefusalDetector
// ---------------------------------------------------------------------------

/// <summary>
/// Detects model refusal patterns ("I cannot", "As an AI", etc.).
/// Error severity → triggers fallback.
/// </summary>
public sealed class RefusalDetector : IOutputValidator
{
    public string Name => "RefusalDetector";

    private static readonly string[] RefusalPhrases =
    [
        "i cannot", "i can't", "i am unable", "i'm unable",
        "as an ai", "as an artificial intelligence",
        "i'm not able", "i am not able",
        "i won't", "i will not",
        "that's not something i",
        "my guidelines", "my content policy"
    ];

    public Task<ValidationResult> ValidateAsync(string output, TaskContext context, CancellationToken ct = default)
    {
        var lower = output.ToLowerInvariant();
        foreach (var phrase in RefusalPhrases)
        {
            if (lower.Contains(phrase))
                return Task.FromResult(new ValidationResult
                {
                    Severity = ValidationSeverity.Error,
                    ValidatorName = Name,
                    Message = $"Model refusal detected: '{phrase}'"
                });
        }

        return Task.FromResult(new ValidationResult
        {
            Severity = ValidationSeverity.Pass,
            ValidatorName = Name,
            Message = "No refusal patterns detected."
        });
    }
}

// ---------------------------------------------------------------------------
// Validator: StructuredOutput
// ---------------------------------------------------------------------------

/// <summary>
/// When ExpectsStructuredOutput is true, validates that the output is valid JSON.
/// Skips validation for non-structured task types.
/// </summary>
public sealed class StructuredOutputValidator : IOutputValidator
{
    public string Name => "StructuredOutput";

    public Task<ValidationResult> ValidateAsync(string output, TaskContext context, CancellationToken ct = default)
    {
        if (!context.ExpectsStructuredOutput)
            return Task.FromResult(new ValidationResult
            {
                Severity = ValidationSeverity.Pass,
                ValidatorName = Name,
                Message = "Structured output not expected; skipped."
            });

        // Find first { or [ and attempt parse from there
        var start = output.IndexOfAny(['{', '[']);
        if (start < 0)
            return Task.FromResult(new ValidationResult
            {
                Severity = ValidationSeverity.Error,
                ValidatorName = Name,
                Message = "No JSON object or array found in output."
            });

        try
        {
            System.Text.Json.JsonDocument.Parse(output[start..]);
            return Task.FromResult(new ValidationResult
            {
                Severity = ValidationSeverity.Pass,
                ValidatorName = Name,
                Message = "Valid JSON."
            });
        }
        catch (System.Text.Json.JsonException ex)
        {
            return Task.FromResult(new ValidationResult
            {
                Severity = ValidationSeverity.Error,
                ValidatorName = Name,
                Message = $"Invalid JSON: {ex.Message}"
            });
        }
    }
}

// ---------------------------------------------------------------------------
// Validator: CodeSyntax (lightweight bracket/delimiter balance check)
// ---------------------------------------------------------------------------

/// <summary>
/// Applies to code-related tasks. Checks bracket balance and unclosed strings.
/// Intentionally lightweight — no Roslyn compilation required.
/// </summary>
public sealed class CodeSyntaxValidator : IOutputValidator
{
    public string Name => "CodeSyntax";

    private static readonly TaskType[] CodeTasks =
        [TaskType.Review, TaskType.Refactor, TaskType.TestGeneration, TaskType.AgentStep];

    public Task<ValidationResult> ValidateAsync(string output, TaskContext context, CancellationToken ct = default)
    {
        if (!CodeTasks.Contains(context.TaskType))
            return Task.FromResult(new ValidationResult
            {
                Severity = ValidationSeverity.Pass,
                ValidatorName = Name,
                Message = "Not a code task; skipped."
            });

        var error = CheckBalance(output);
        if (error is not null)
            return Task.FromResult(new ValidationResult
            {
                Severity = ValidationSeverity.Error,
                ValidatorName = Name,
                Message = error
            });

        return Task.FromResult(new ValidationResult
        {
            Severity = ValidationSeverity.Pass,
            ValidatorName = Name,
            Message = "Bracket balance OK."
        });
    }

    private static string? CheckBalance(string code)
    {
        var stack = new Stack<char>();
        var inString = false;
        var stringChar = '\0';

        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];

            // Track string literals (skip escaped chars)
            if (inString)
            {
                if (c == '\\') { i++; continue; } // skip escaped char
                if (c == stringChar) inString = false;
                continue;
            }

            if (c is '"' or '\'')
            {
                inString = true;
                stringChar = c;
                continue;
            }

            if (c is '{' or '(' or '[') { stack.Push(c); continue; }

            if (c is '}' or ')' or ']')
            {
                if (stack.Count == 0)
                    return $"Unexpected closing '{c}' at position {i}.";
                var open = stack.Pop();
                if (!Matches(open, c))
                    return $"Mismatched bracket: opened '{open}', closed '{c}' at position {i}.";
            }
        }

        if (inString) return "Unclosed string literal.";
        if (stack.Count > 0) return $"Unclosed bracket '{stack.Peek()}'.";
        return null;
    }

    private static bool Matches(char open, char close) => (open, close) switch
    {
        ('{', '}') => true,
        ('(', ')') => true,
        ('[', ']') => true,
        _ => false
    };
}
