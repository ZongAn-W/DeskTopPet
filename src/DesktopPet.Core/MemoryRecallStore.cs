using System.Text;
using System.Text.Json;

namespace DesktopPet.Core;

/// <summary>
/// Remembers which notes the last reply received. Purely informational: a missing or corrupt file
/// only costs the panel its explanation, never a memory.
/// </summary>
public sealed class MemoryRecallStore(string path)
{
    public string FilePath { get; } = Path.GetFullPath(path);

    public MemoryDigestReport? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize<MemoryDigestReport>(File.ReadAllText(FilePath, Encoding.UTF8));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(MemoryDigestReport report)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(report), new UTF8Encoding(false));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The recall trace is a convenience, never a reason to interrupt a reply.
        }
    }
}
