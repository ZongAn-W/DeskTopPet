using System.Text.Json;

namespace DesktopPet.Core;

public sealed class PreferencesStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PreferencesStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPet", "preferences.json");
    }

    public PetPreferences Load()
    {
        try
        {
            if (!File.Exists(_path)) return PetPreferences.Default;
            // Editors and shells (Notepad, `Set-Content -Encoding utf8`) write a UTF-8 BOM by
            // default, and System.Text.Json rejects one as an invalid value start rather than
            // skipping it. Strip it explicitly so a BOM cannot silently discard every setting.
            var bytes = File.ReadAllBytes(_path);
            var payload = bytes.AsSpan();
            if (payload.Length >= 3 && payload[0] == 0xEF && payload[1] == 0xBB && payload[2] == 0xBF)
                payload = payload[3..];
            var preferences = JsonSerializer.Deserialize<PetPreferences>(payload, Options);
            return preferences is null ? PetPreferences.Default : Normalize(preferences);
        }
        catch { return PetPreferences.Default; }
    }

    public void Save(PetPreferences preferences)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Normalize(preferences), Options));
            File.Move(temporary, _path, true);
        }
        catch { }
    }

    private static PetPreferences Normalize(PetPreferences preferences) => preferences with
    {
        LeftWalkSpeed = WalkSpeedOptions.Normalize(preferences.LeftWalkSpeed),
        RightWalkSpeed = WalkSpeedOptions.Normalize(preferences.RightWalkSpeed)
    };
}
