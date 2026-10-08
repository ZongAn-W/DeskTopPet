using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopPet.Core;

namespace DesktopPet;

public sealed class ChatSettingsStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPet", "ai-settings.bin");

    internal string MemoryPath => path is null
        ? Path.Combine(Path.GetDirectoryName(_path)!, "memory.md")
        : _path + ".memory.md";

    public ChatSettings Load()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            var json = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            return (JsonSerializer.Deserialize<ChatSettings>(json) ?? throw new JsonException()).Normalize();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            throw new InvalidDataException("无法读取保存的 AI 设置，请重新填写并保存。", error);
        }
    }

    public void Save(ChatSettings settings)
    {
        var encrypted = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(settings.Normalize()), null, DataProtectionScope.CurrentUser);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllBytes(temporary, encrypted);
        File.Move(temporary, _path, overwrite: true);
    }
}
