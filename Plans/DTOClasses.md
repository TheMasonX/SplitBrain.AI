This is the layer that prevents your system from turning into “stringly-typed chaos.”
Here are **clean, production-ready DTOs + FluentValidation rules**, aligned exactly with the schemas defined.

No fluff—this is meant to drop into a .NET 10 project and work.

---

# 16. C# DTOs + Validation Layer

---

# 16.1 Base Infrastructure

## Common Interfaces

```csharp
public interface IMcpRequest
{
    string Version { get; init; }
}

public interface IMcpResponse
{
    Meta Meta { get; init; }
    McpError? Error { get; init; }
}
```

---

## Shared Models

```csharp
public sealed class Meta
{
    public string TaskId { get; init; } = default!;
    public string Node { get; init; } = default!; // "A" | "B"
    public string Model { get; init; } = default!;
    public int LatencyMs { get; init; }
    public int TokensIn { get; init; }
    public int TokensOut { get; init; }
}
```

---

```csharp
public sealed class McpError
{
    public string Code { get; init; } = default!;
    public string Message { get; init; } = default!;
    public bool Retryable { get; init; }
    public Dictionary<string, object>? Details { get; init; }
}
```

---

## Diff Models

```csharp
public sealed class Diff
{
    public List<DiffFile> Files { get; init; } = new();
}

public sealed class DiffFile
{
    public string Path { get; init; } = default!;
    public string ChangeType { get; init; } = default!; // modify | create | delete
    public string Patch { get; init; } = default!;
}
```

---

# 16.2 review_code

## DTOs

```csharp
public sealed class ReviewCodeRequest : IMcpRequest
{
    public string Version { get; init; } = "1.0";
    public string Code { get; init; } = default!;
    public string Language { get; init; } = default!;
    public string Focus { get; init; } = default!;

    public List<RelatedFile>? Context { get; init; }
}

public sealed class RelatedFile
{
    public string Path { get; init; } = default!;
    public string Content { get; init; } = default!;
}
```

---

```csharp
public sealed class ReviewCodeResponse : IMcpResponse
{
    public string Summary { get; init; } = default!;
    public List<ReviewIssue> Issues { get; init; } = new();
    public Diff? SuggestedDiff { get; init; }

    public Meta Meta { get; init; } = default!;
    public McpError? Error { get; init; }
}

public sealed class ReviewIssue
{
    public string Severity { get; init; } = default!;
    public string Type { get; init; } = default!;
    public string Message { get; init; } = default!;
    public IssueLocation Location { get; init; } = default!;
    public string Suggestion { get; init; } = default!;
}

public sealed class IssueLocation
{
    public string File { get; init; } = default!;
    public int LineStart { get; init; }
    public int LineEnd { get; init; }
}
```

---

## Validator

```csharp
using FluentValidation;

public sealed class ReviewCodeRequestValidator : AbstractValidator<ReviewCodeRequest>
{
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
            .Must(x => new[] { "architecture", "performance", "bugs", "readability", "security" }
                .Contains(x))
            .WithMessage("Invalid focus value");

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
```

---

# 16.3 refactor_code

## DTOs

```csharp
public sealed class RefactorCodeRequest : IMcpRequest
{
    public string Version { get; init; } = "1.0";
    public string Goal { get; init; } = default!;
    public List<CodeFile> Codebase { get; init; } = new();
    public RefactorConstraints Constraints { get; init; } = new();
}

public sealed class CodeFile
{
    public string Path { get; init; } = default!;
    public string Content { get; init; } = default!;
}

public sealed class RefactorConstraints
{
    public bool PreserveBehavior { get; init; } = true;
    public int MaxFiles { get; init; } = 10;
}
```

---

## Validator

```csharp
public sealed class RefactorCodeRequestValidator : AbstractValidator<RefactorCodeRequest>
{
    public RefactorCodeRequestValidator()
    {
        RuleFor(x => x.Goal).NotEmpty();

        RuleFor(x => x.Codebase)
            .NotEmpty()
            .Must(x => x.Count <= 20);

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
```

---

# 16.4 apply_patch

## DTO

```csharp
public sealed class ApplyPatchRequest : IMcpRequest
{
    public string Version { get; init; } = "1.0";
    public Diff Diff { get; init; } = new();
    public bool DryRun { get; init; }
}
```

---

## Validator

```csharp
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
            .Must(x => x is "modify" or "create" or "delete");

        RuleFor(x => x.Patch)
            .NotEmpty()
            .When(x => x.ChangeType != "delete");
    }
}
```

---

# 16.5 run_tests

## DTO

```csharp
public sealed class RunTestsRequest : IMcpRequest
{
    public string Version { get; init; } = "1.0";
    public string ProjectPath { get; init; } = default!;
    public string? TestFilter { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
}
```

---

## Validator

```csharp
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
```

---

# 16.6 search_codebase

## DTO

```csharp
public sealed class SearchCodebaseRequest : IMcpRequest
{
    public string Version { get; init; } = "1.0";
    public string Query { get; init; } = default!;
    public int TopK { get; init; } = 5;
    public SearchFilters? Filters { get; init; }
}

public sealed class SearchFilters
{
    public string? Path { get; init; }
    public string? Language { get; init; }
}
```

---

## Validator

```csharp
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
```

---

# 16.7 System.Text.Json Configuration

---

## Serializer Setup

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

public static class JsonConfig
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };
}
```

---

## Example Usage

```csharp
var json = JsonSerializer.Serialize(request, JsonConfig.Default);

var obj = JsonSerializer.Deserialize<ReviewCodeRequest>(json, JsonConfig.Default);
```

---

# 16.8 Validation Pipeline Integration (Critical)

---

## Extension Method

```csharp
public static class ValidationExtensions
{
    public static void ValidateOrThrow<T>(this T instance, IValidator<T> validator)
    {
        var result = validator.Validate(instance);

        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }
}
```

---

## Usage in Handler

```csharp
public async Task<ReviewCodeResponse> Handle(ReviewCodeRequest request)
{
    request.ValidateOrThrow(new ReviewCodeRequestValidator());

    // proceed safely
}
```

---

# Final Notes

This layer gives you:

* **Compile-time safety** (DTOs)
* **Runtime guarantees** (FluentValidation)
* **Clean serialization boundaries** (System.Text.Json)
* **Predictable contracts for agents**