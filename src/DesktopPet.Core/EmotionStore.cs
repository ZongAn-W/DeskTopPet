using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopPet.Core;

public sealed class EmotionStore(string path)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public bool LoadFailed { get; private set; }

    public EmotionState Load(DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(Path)) return EmotionState.Calm(now);
            var state = JsonSerializer.Deserialize<EmotionState>(File.ReadAllText(Path, Encoding.UTF8), Options)
                ?? throw new JsonException();
            return state.At(now);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadFailed = true;
            return EmotionState.Calm(now);
        }
    }

    public void Save(EmotionState state)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options), new UTF8Encoding(false));
        File.Move(temporary, Path, overwrite: true);
    }
}
