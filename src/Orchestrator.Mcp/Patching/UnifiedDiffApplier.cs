namespace Orchestrator.Mcp.Patching;

/// <summary>
/// Applies unified diff patches (output of `diff -u`) to in-memory text without
/// spawning any external process.  Supports context, add (+) and remove (-) lines.
/// </summary>
internal static class UnifiedDiffApplier
{
    /// <summary>
    /// Applies <paramref name="patch"/> to <paramref name="originalText"/> and returns
    /// the patched text, or throws <see cref="PatchException"/> on failure.
    /// </summary>
    public static string Apply(string originalText, string patch)
    {
        var lines = originalText.Split('\n');
        // Normalise — remove trailing \r so index arithmetic works on either line ending
        for (var i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd('\r');

        var hunks = ParseHunks(patch);
        var result = new List<string>(lines);
        var offset = 0; // cumulative line-count delta from previously applied hunks

        foreach (var hunk in hunks)
        {
            var applyAt = hunk.OriginalStart - 1 + offset; // 0-based
            var idx = applyAt;

            // Verify context / removed lines match
            var verifyLines = hunk.Lines.Where(l => l.Kind != LineKind.Add).ToList();
            if (idx + verifyLines.Count > result.Count)
                throw new PatchException($"Hunk starting at line {hunk.OriginalStart} extends beyond file end.");

            for (var vi = 0; vi < verifyLines.Count; vi++)
            {
                var expected = verifyLines[vi].Text;
                var actual = result[idx + vi];
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                    throw new PatchException(
                        $"Hunk mismatch at line {idx + vi + 1}: expected '{expected}', found '{actual}'.");
            }

            // Apply hunk — walk backwards through the hunk lines to keep index stable
            var writeIdx = applyAt;
            var hunkDelta = 0;
            foreach (var hunkLine in hunk.Lines)
            {
                switch (hunkLine.Kind)
                {
                    case LineKind.Context:
                        writeIdx++;
                        break;
                    case LineKind.Remove:
                        result.RemoveAt(writeIdx);
                        hunkDelta--;
                        break;
                    case LineKind.Add:
                        result.Insert(writeIdx, hunkLine.Text);
                        writeIdx++;
                        hunkDelta++;
                        break;
                }
            }

            offset += hunkDelta;
        }

        return string.Join("\n", result);
    }

    // -------------------------------------------------------------------------
    // Parser
    // -------------------------------------------------------------------------

    private static List<Hunk> ParseHunks(string patch)
    {
        var hunks = new List<Hunk>();
        Hunk? current = null;

        foreach (var rawLine in patch.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                if (current != null) hunks.Add(current);
                current = ParseHunkHeader(line);
                continue;
            }

            if (current == null) continue; // skip file header lines (--- / +++)

            if (line.StartsWith('+') && !line.StartsWith("+++"))
                current.Lines.Add(new HunkLine(LineKind.Add, line[1..]));
            else if (line.StartsWith('-') && !line.StartsWith("---"))
                current.Lines.Add(new HunkLine(LineKind.Remove, line[1..]));
            else if (line.StartsWith(' '))
                current.Lines.Add(new HunkLine(LineKind.Context, line[1..]));
            else if (line == "\\ No newline at end of file")
                current.NoNewlineAtEnd = true;
        }

        if (current != null) hunks.Add(current);
        return hunks;
    }

    private static Hunk ParseHunkHeader(string header)
    {
        // Example: @@ -10,6 +10,7 @@ optional context
        var parts = header.Split(' ');
        var orig = parts[1]; // e.g. -10,6
        var start = int.Parse(orig.TrimStart('-').Split(',')[0]);
        return new Hunk { OriginalStart = start };
    }

    // -------------------------------------------------------------------------
    // Internal types
    // -------------------------------------------------------------------------

    private sealed class Hunk
    {
        public int OriginalStart { get; init; }
        public List<HunkLine> Lines { get; } = new();
        public bool NoNewlineAtEnd { get; set; }
    }

    private readonly record struct HunkLine(LineKind Kind, string Text);

    private enum LineKind { Context, Add, Remove }
}

public sealed class PatchException : Exception
{
    public PatchException(string message) : base(message) { }
}
