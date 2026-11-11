using System.Globalization;
using System.Text;

namespace DesktopPet.Core;

/// <summary>
/// Keeps the last few versions of the memory document before it is rewritten, so a mistaken
/// summary or edit can be recovered by hand. Backups never block the save itself.
/// </summary>
public sealed class MemoryBackup(string directory, int keep = 10)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public int Keep { get; } = Math.Max(1, keep);

    public string? Capture(string? document)
    {
        if (string.IsNullOrWhiteSpace(document)) return null;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            var target = Path.Combine(DirectoryPath, stamp + ".md");
            for (var index = 2; File.Exists(target); index++)
                target = Path.Combine(DirectoryPath, stamp + "-" + index + ".md");
            File.WriteAllText(target, document, new UTF8Encoding(false));
            Prune();
            return target;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Prune()
    {
        var files = new DirectoryInfo(DirectoryPath).GetFiles("*.md")
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .ToList();
        foreach (var file in files.Skip(Keep))
        {
            try { file.Delete(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
