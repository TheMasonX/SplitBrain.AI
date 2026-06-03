using System.Text.Json;
using System.Text.RegularExpressions;
using Orchestrator.Core.Serialization;

#pragma warning disable CS1591  // Missing XML comment for publicly visible type or member

namespace Orchestrator.Mcp.Helpers;

/// <summary>
/// Maximally forgiving response post-processor for LLM codegen tool outputs.
///
/// LLMs frequently wrap generated code in markdown fences, add preamble
/// ("Here's the refactored version:"), or include trailing explanation
/// ("Key changes: ..."). This class extracts the clean content the caller
/// actually needs without requiring the model to follow strict output formatting.
///
/// Design principles:
///   1. Never return empty when input is non-empty (always produce something)
///   2. Prefer the last/largest code block when multiple are present
///   3. Never modify the extracted code itself (only strip outer prose)
///   4. Strip operations are language-agnostic where possible
/// </summary>
public static class ResponseCleaner
{
    // Known prose starters that indicate preamble before code
    private static readonly string[] PreamblePhrases =
    [
        "here is", "here's", "here are", "below is", "below are",
        "the refactored", "the updated", "the revised", "the following",
        "i have refactored", "i've refactored", "i refactored",
        "i have generated", "i've generated", "the generated",
        "sure!", "sure,", "of course", "certainly",
        "```", // in case of trailing newline before fence
    ];

    // Known prose starters that indicate outro after code
    private static readonly string[] OutroPhrases =
    [
        "key changes", "the key change", "changes made",
        "what changed", "explanation:", "note:", "notes:",
        "in this refactoring", "in this version",
        "i changed", "i updated", "i modified", "i renamed",
        "improvements:", "summary:", "let me know",
    ];

    // ── ExtractCode ──────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts code from a model response that may contain markdown fences,
    /// preamble prose, and/or trailing explanation.
    ///
    /// Strategy:
    ///   1. If fenced blocks exist → return the largest/last code block
    ///   2. If no fences but text looks like code → return as-is
    ///   3. If no fences and text looks like prose + code → heuristic extract
    ///   4. Fallback → return original text trimmed
    /// </summary>
    /// <param name="raw">Raw model response.</param>
    /// <param name="language">Hint for recognising language-specific code starters.</param>
    public static string ExtractCode(string raw, string language = "")
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        var trimmed = raw.Trim();

        // Strategy 1: extract from code fences
        var fenceBlocks = ExtractFenceBlocks(trimmed);
        if (fenceBlocks.Count > 0)
        {
            // Prefer the largest block; on length tie, take the last (most likely the final answer)
            var best = fenceBlocks
                .Select((block, idx) => (block, idx))
                .OrderByDescending(x => x.block.Length)
                .ThenByDescending(x => x.idx)  // highest index = last occurrence
                .First()
                .block;
            return best.TrimEnd();
        }

        // Strategy 2: text already looks like pure code
        if (LooksLikeCode(trimmed, language))
            return trimmed;

        // Strategy 3: strip preamble + outro from prose-wrapped code
        var stripped = StripProseWrapper(trimmed, language);
        if (stripped.Length > 0 && stripped.Length < trimmed.Length)
            return stripped.TrimEnd();

        // Strategy 4: fallback — return original (never empty)
        return trimmed;
    }

    // ── StripFences ──────────────────────────────────────────────────────────

    /// <summary>
    /// Removes markdown code fences (``` ... ```) from a string,
    /// preserving the content inside. Does NOT strip prose.
    /// Useful when you want the raw model response but without markdown formatting.
    /// </summary>
    public static string StripFences(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        var fenceBlocks = ExtractFenceBlocks(raw.Trim());
        if (fenceBlocks.Count == 0) return raw.Trim();

        // Re-join all extracted blocks separated by blank lines
        return string.Join("\n\n", fenceBlocks).TrimEnd();
    }

    // ── TryExtractJson ───────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to parse JSON from a model response that may have markdown fences
    /// or prose around it. Tries several extraction strategies before giving up.
    /// </summary>
    public static bool TryExtractJson<T>(string raw, out T? result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var candidates = new List<string>();

        // 1. Try the whole string
        candidates.Add(raw.Trim());

        // 2. Extract from code fences (```json or ``` + JSON content)
        foreach (var block in ExtractFenceBlocks(raw))
            candidates.Add(block.Trim());

        // 3. Find raw JSON-looking substring ([...] or {...})
        var jsonStart = raw.IndexOfAny(['[', '{']);
        if (jsonStart >= 0)
        {
            var jsonEnd = raw.LastIndexOfAny([']', '}']);
            if (jsonEnd > jsonStart)
                candidates.Add(raw[jsonStart..(jsonEnd + 1)]);
        }

        foreach (var candidate in candidates)
        {
            try
            {
                result = JsonSerializer.Deserialize<T>(candidate, JsonConfig.Default);
                if (result is not null) return true;
            }
            catch { /* try next */ }
        }

        return false;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Extracts all content blocks from markdown code fences.</summary>
    private static List<string> ExtractFenceBlocks(string text)
    {
        var blocks = new List<string>();

        // Pattern: ```[language]\n content \n```
        // Handles nested backticks by looking for balanced triple-backtick pairs
        var pattern = new Regex(
            @"```(?:[a-zA-Z0-9_\-+#.]*)?\r?\n(.*?)\r?\n```",
            RegexOptions.Singleline | RegexOptions.Compiled);

        foreach (Match m in pattern.Matches(text))
        {
            var content = m.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(content))
                blocks.Add(content);
        }

        // Also handle ``` without newline after language tag (some models omit it)
        if (blocks.Count == 0)
        {
            var loosePattern = new Regex(
                @"```(?:[a-zA-Z0-9_\-+#.]*)\s+(.*?)```",
                RegexOptions.Singleline | RegexOptions.Compiled);
            foreach (Match m in loosePattern.Matches(text))
            {
                var content = m.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(content))
                    blocks.Add(content);
            }
        }

        return blocks;
    }

    /// <summary>
    /// Returns true if the text appears to be source code rather than prose.
    /// Heuristic: starts with common code constructs.
    /// </summary>
    private static bool LooksLikeCode(string text, string language)
    {
        var lower = language.ToLowerInvariant();
        var first = text.TrimStart().Length > 0 ? text.TrimStart()[..Math.Min(200, text.TrimStart().Length)] : "";

        // Generic code starters common across languages
        string[] codeStarters =
        [
            "using ", "import ", "from ", "require(", "include ",
            "public ", "private ", "protected ", "internal ",
            "class ", "struct ", "interface ", "enum ", "namespace ",
            "function ", "def ", "fn ", "func ",
            "#include", "#pragma", "#define",
            "var ", "let ", "const ", "type ",
            "module ", "export ", "package ",
            "@", "---", "<!",  // YAML, HTML, diff headers
        ];

        return codeStarters.Any(s => first.StartsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Heuristic: strips a prose preamble and/or outro from a mixed prose+code response.
    /// Finds the first line that looks like the start of code and the last line that
    /// looks like code, and returns everything between.
    /// </summary>
    private static string StripProseWrapper(string text, string language)
    {
        var lines = text.Split('\n');

        // Find first code-looking line (skip prose preamble)
        int codeStart = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // If this line starts with a known preamble phrase, skip it
            bool isPreamble = PreamblePhrases.Any(p => line.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (isPreamble) { codeStart = i + 1; continue; }

            // If this line looks like code, we've found the start
            if (LooksLikeCode(line, language))
            {
                codeStart = i;
                break;
            }
        }

        // Find last code-looking line (stop before prose outro)
        int codeEnd = lines.Length - 1;
        for (int i = lines.Length - 1; i >= codeStart; i--)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            bool isOutro = OutroPhrases.Any(p => line.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (isOutro) { codeEnd = i - 1; continue; }

            codeEnd = i;
            break;
        }

        if (codeEnd < codeStart) return string.Empty;

        return string.Join('\n', lines[codeStart..(codeEnd + 1)]);
    }
}

