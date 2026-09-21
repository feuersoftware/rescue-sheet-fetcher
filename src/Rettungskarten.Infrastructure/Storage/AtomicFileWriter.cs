using System.Text.Json;

namespace Rettungskarten.Infrastructure.Storage;

/// <summary>
/// Writes a file to a temp path in the same directory, then atomically renames it into place -
/// so a process kill (Ctrl+C, OOM) mid-write can never leave a half-written, truncated file at the
/// real path for a later run to trip over. <see cref="File.Move(string, string, bool)"/> with
/// overwrite is atomic on both Windows (MoveFileEx/MOVEFILE_REPLACE_EXISTING) and POSIX (rename())
/// for a same-volume move, which placing the temp file next to its destination guarantees.
/// </summary>
internal static class AtomicFileWriter
{
    public static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
    {
        await WriteAsync(path, async tempStream => await JsonSerializer.SerializeAsync(tempStream, value, JsonDefaults.Options, ct));
    }

    public static async Task WriteBytesAsync(string path, byte[] content, CancellationToken ct)
    {
        await WriteAsync(path, async tempStream => await tempStream.WriteAsync(content, ct));
    }

    private static async Task WriteAsync(string path, Func<FileStream, Task> writeContent)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var tempStream = File.Create(tempPath))
            {
                await writeContent(tempStream);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }
}
