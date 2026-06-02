using System.Text;

namespace Orchestrator.Infrastructure.Configuration;

/// <summary>
/// Writes JSON content atomically by writing to a temporary file then replacing the target file.
/// </summary>
internal static class AtomicJsonFileWriter
{
    public static async Task WriteAsync(string targetPath, string content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetPath);
        ArgumentNullException.ThrowIfNull(content);
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        cancellationToken.ThrowIfCancellationRequested();
        var tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(targetPath)) File.Move(tempPath, targetPath, overwrite: true);
            else File.Move(tempPath, targetPath);
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }
}
