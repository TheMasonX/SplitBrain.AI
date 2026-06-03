namespace Orchestrator.Core.Utilities;

/// <summary>
/// Utilities for cleaning and extracting structured content from LLM responses.
/// Models frequently wrap JSON in markdown fences even when instructed not to.
/// </summary>
public static class ResponseParser
{
    /// <summary>
    /// Strips markdown code fences from a string that may or may not be wrapped.
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
    /// Tries to extract a top-level JSON object or array from a response string.
    /// Strips fences, then tries to parse. Falls back to wrapping in a default envelope.
    /// </summary>
    public static (bool success, string json) TryExtractJson(string text, string fallbackKey = "text")
    {
        var stripped = StripFences(text);
        if (stripped.StartsWith("{") || stripped.StartsWith("["))
        {
            return (true, stripped);
        }
        // Plain text fallback: wrap in JSON object
        var escaped = System.Text.Json.JsonSerializer.Serialize(stripped);
        return (false, $$"""{"{{fallbackKey}}": {{escaped}}}""");
    }
}
