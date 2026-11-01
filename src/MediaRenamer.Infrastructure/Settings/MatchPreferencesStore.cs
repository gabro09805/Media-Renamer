using System.Text.Json;
using System.Text.Json.Serialization;
using MediaRenamer.Model;

namespace MediaRenamer.Infrastructure.Settings;

/// <summary>Persists the Match Preferences (output language, match mode, episode order).</summary>
public sealed class MatchPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public MatchPreferencesStore(string path)
    {
        _path = path;
        Current = Load();
    }

    public MatchPreferences Current { get; private set; }

    public event Action<MatchPreferences>? Changed;

    public void Save(MatchPreferences value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, _path, overwrite: true);
        Current = value;
        Changed?.Invoke(value);
    }

    private MatchPreferences Load()
    {
        if (!File.Exists(_path)) return new MatchPreferences();
        try
        {
            var loaded = JsonSerializer.Deserialize<MatchPreferences>(File.ReadAllText(_path), Options);
            return loaded is null || string.IsNullOrWhiteSpace(loaded.OutputLanguage) ? new MatchPreferences() : loaded;
        }
        catch (JsonException)
        {
            return new MatchPreferences();
        }
    }
}
