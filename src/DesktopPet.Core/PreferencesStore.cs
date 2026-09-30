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
            return JsonSerializer.Deserialize<PetPreferences>(File.ReadAllText(_path), Options) ?? PetPreferences.Default;
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
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, Options));
            File.Move(temporary, _path, true);
        }
        catch { }
    }
}
