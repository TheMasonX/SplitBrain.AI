namespace Orchestrator.Core.Utilities;

/// <summary>
/// Lightweight utilities for cleaning LLM response text.
/// Lives in Core so all projects can reference it.
/// For more aggressive extraction (preamble stripping, multi-block handling),
/// see <c>Orchestrator.Mcp.Helpers.ResponseCleaner</c>.
/// </summary>
public static class ResponseParser
{
    /// <summary>
    /// Strips the outermost markdown code fence from a response.
    /// Handles: ```json ... ```, ```csharp ... ```, ``` ... ```, plain text.
    /// </summary>
    public static string StripFences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```")) return trimmed;
        var firstNewLine = trimmed.IndexOf('\n');
        if (firstNewLine < 0) return trimmed;
        var inner = trimmed[(firstNewLine + 1)..];
        var lastFence = inner.LastIndexOf("```");
        return lastFence >= 0 ? inner[..lastFence].Trim() : inner.Trim();
    }

    /// <summary>
    /// Tries to extract a JSON object or array from a response string.
    /// Strips fences then locates the first { or [ character.
    /// </summary>
    public static (bool Success, string Json) TryExtractJson(
        string text,
        string fallbackKey = "text")
    {
        var stripped = StripFences(text);
        if (stripped.StartsWith("{") || stripped.StartsWith("["))
            return (true, stripped);

        // Plain-text fallback: wrap in a JSON object
        var escaped = System.Text.Json.JsonSerializer.Serialize(stripped);
        return (false, $"{{\"{fallbackKey}\": {escaped}}}");
    }
}
