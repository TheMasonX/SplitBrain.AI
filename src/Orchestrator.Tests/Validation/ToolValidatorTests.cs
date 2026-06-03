using Orchestrator.Core.Models;
using Orchestrator.Core.Validation;

namespace Orchestrator.Tests.Validation;

public sealed class RefactorCodeRequestValidatorTests
{
    private readonly RefactorCodeRequestValidator _validator = new();

    private static RefactorCodeRequest Valid() => new()
    {
        Goal = "Extract method",
        Codebase = [new CodeFile { Path = "Foo.cs", Content = "public class Foo {}" }],
        Constraints = new RefactorConstraints { PreserveBehavior = true, MaxFiles = 10 }
    };

    [Test]
    public void ValidRequest_PassesValidation()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [TestCase("")]
    [TestCase("   ")]
    public void EmptyGoal_FailsValidation(string goal)
    {
        var request = new RefactorCodeRequest
        {
            Goal = goal,
            Codebase = Valid().Codebase,
            Constraints = Valid().Constraints
        };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RefactorCodeRequest.Goal));
    }

    [Test]
    public void EmptyCodebase_FailsValidation()
    {
        var request = new RefactorCodeRequest { Goal = "Extract method", Codebase = [], Constraints = new() };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RefactorCodeRequest.Codebase));
    }

    [Test]
    public void CodebaseExceeding20Files_FailsValidation()
    {
        var files = Enumerable.Range(0, 21)
            .Select(i => new CodeFile { Path = $"File{i}.cs", Content = "x" })
            .ToList();
        var request = new RefactorCodeRequest { Goal = "Extract method", Codebase = files, Constraints = new() };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }

    [Test]
    public void MaxFilesZero_FailsValidation()
    {
        var request = new RefactorCodeRequest
        {
            Goal = "Extract method",
            Codebase = Valid().Codebase,
            Constraints = new RefactorConstraints { MaxFiles = 0 }
        };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }

    [Test]
    public void MaxFilesExceeding50_FailsValidation()
    {
        var request = new RefactorCodeRequest
        {
            Goal = "Extract method",
            Codebase = Valid().Codebase,
            Constraints = new RefactorConstraints { MaxFiles = 51 }
        };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
    }
}

public sealed class RunTestsRequestValidatorTests
{
    private readonly RunTestsRequestValidator _validator = new();

    [Test]
    public void ValidRequest_PassesValidation()
    {
        var request = new RunTestsRequest { ProjectPath = "MyProject.csproj", TimeoutSeconds = 30 };
        _validator.Validate(request).IsValid.Should().BeTrue();
    }

    [TestCase("")]
    [TestCase("   ")]
    public void EmptyProjectPath_FailsValidation(string path)
    {
        var request = new RunTestsRequest { ProjectPath = path, TimeoutSeconds = 30 };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RunTestsRequest.ProjectPath));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(121)]
    public void OutOfRangeTimeout_FailsValidation(int timeout)
    {
        var request = new RunTestsRequest { ProjectPath = "MyProject.csproj", TimeoutSeconds = timeout };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RunTestsRequest.TimeoutSeconds));
    }

    [TestCase(1)]
    [TestCase(60)]
    [TestCase(120)]
    public void BoundaryTimeout_PassesValidation(int timeout)
    {
        var request = new RunTestsRequest { ProjectPath = "MyProject.csproj", TimeoutSeconds = timeout };
        _validator.Validate(request).IsValid.Should().BeTrue();
    }
}

public sealed class SearchCodebaseRequestValidatorTests
{
    private readonly SearchCodebaseRequestValidator _validator = new();

    private static SearchCodebaseRequest Valid() => new()
    {
        Query = "dependency injection",
        TopK = 5
    };

    [Test]
    public void ValidRequest_PassesValidation()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [TestCase("")]
    [TestCase("   ")]
    public void EmptyQuery_FailsValidation(string query)
    {
        var request = new SearchCodebaseRequest { Query = query, TopK = 5 };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SearchCodebaseRequest.Query));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(21)]
    public void OutOfRangeTopK_FailsValidation(int topK)
    {
        var request = new SearchCodebaseRequest { Query = "test", TopK = topK };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SearchCodebaseRequest.TopK));
    }

    [TestCase(1)]
    [TestCase(10)]
    [TestCase(20)]
    public void BoundaryTopK_PassesValidation(int topK)
    {
        var request = new SearchCodebaseRequest { Query = "test", TopK = topK };
        _validator.Validate(request).IsValid.Should().BeTrue();
    }
}
