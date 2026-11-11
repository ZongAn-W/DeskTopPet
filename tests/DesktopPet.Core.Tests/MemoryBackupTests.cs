using DesktopPet.Core;

namespace DesktopPet.Core.Tests;

public sealed class MemoryBackupTests
{
    [Fact]
    public void CaptureWritesEachVersionAndPrunesTheOldest()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pet-backup-{Guid.NewGuid():N}");
        try
        {
            var backup = new MemoryBackup(directory, keep: 3);
            for (var index = 0; index < 5; index++)
                backup.Capture($"# Desktop Pet Memory\n\n- 版本 {index}。\n");

            var files = Directory.GetFiles(directory, "*.md");
            Assert.Equal(3, files.Length);
            Assert.All(files, file => Assert.Contains("版本", File.ReadAllText(file)));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BlankDocumentCreatesNoBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pet-backup-{Guid.NewGuid():N}");
        try
        {
            var backup = new MemoryBackup(directory);
            Assert.Null(backup.Capture(""));
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
