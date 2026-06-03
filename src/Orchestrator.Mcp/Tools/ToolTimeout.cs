using Microsoft.Extensions.Configuration;

namespace Orchestrator.Mcp.Tools;

/// <summary>
/// Per-tool timeout defaults. All values configurable via appsettings Mcp:ToolTimeouts:{ToolName}.
/// </summary>
public static class ToolTimeout
{
    public const int ReviewCodeSeconds    = 60;
    public const int RefactorCodeSeconds  = 90;
    public const int GenerateTestsSeconds = 90;
    public const int SearchCodebaseSeconds = 60;
    public const int ApplyPatchSeconds    = 30;
    public const int RunTestsSeconds      = 120;
    public const int AgentTaskSeconds     = 300;

    public static TimeSpan For(string toolName, IConfiguration? config = null)
    {
        var key = $"Mcp:ToolTimeouts:{toolName}";
        if (config?[key] is string v && int.TryParse(v, out var override_s))
            return TimeSpan.FromSeconds(override_s);
        return toolName switch
        {
            "review_code"      => TimeSpan.FromSeconds(ReviewCodeSeconds),
            "refactor_code"    => TimeSpan.FromSeconds(RefactorCodeSeconds),
            "generate_tests"   => TimeSpan.FromSeconds(GenerateTestsSeconds),
            "search_codebase"  => TimeSpan.FromSeconds(SearchCodebaseSeconds),
            "apply_patch"      => TimeSpan.FromSeconds(ApplyPatchSeconds),
            "run_tests"        => TimeSpan.FromSeconds(RunTestsSeconds),
            "agent_task"       => TimeSpan.FromSeconds(AgentTaskSeconds),
            _                  => TimeSpan.FromSeconds(60)
        };
    }
}
