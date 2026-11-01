using System.Text.Json;
using MediaRenamer.Plugin.Abstractions;
using MediaRenamer.Platform;

namespace MediaRenamer.Infrastructure.Settings;

/// <summary>
/// Per-provider configuration. Non-secret options are stored per (provider, mode) in a JSON file,
/// credentials once per provider in the <see cref="ISecretStore"/> (TMDB Movie and Episode share the token).
/// </summary>
public sealed class ProviderConfigService
{
    private sealed class Entry
    {
        public bool Enabled { get; set; } = true;
        public Dictionary<string, string> Values { get; set; } = new();
    }

    private readonly string _path;
    private readonly ISecretStore _secrets;
    private readonly object _gate = new();

    public ProviderConfigService(string settingsPath, ISecretStore secrets)
    {
        _path = settingsPath;
        _secrets = secrets;
    }

    public bool IsEnabled(IMediaProvider p)
    {
        lock (_gate) return Load().TryGetValue(p.Key(), out var e) ? e.Enabled : true;
    }

    /// <summary>Stored values merged over schema defaults. Secrets are decrypted.</summary>
    public IReadOnlyDictionary<string, string> GetValues(IMediaProvider p)
    {
        var result = new Dictionary<string, string>();
        Dictionary<string, string> stored;
        lock (_gate) stored = Load().TryGetValue(p.Key(), out var e) ? e.Values : new();

        foreach (var f in p.ConfigSchema)
        {
            string? v = f.Type == ConfigFieldType.Secret
                ? _secrets.Get(SecretKey(p, f))
                : stored.GetValueOrDefault(f.Key);
            v ??= f.DefaultValue;
            if (v is not null) result[f.Key] = v;
        }
        return result;
    }

    public IProviderConfig GetConfig(IMediaProvider p) => new DictionaryProviderConfig(GetValues(p));

    public void Save(IMediaProvider p, bool enabled, IReadOnlyDictionary<string, string> values)
    {
        lock (_gate)
        {
            var all = Load();
            var entry = new Entry { Enabled = enabled };
            foreach (var f in p.ConfigSchema)
            {
                values.TryGetValue(f.Key, out var v);
                if (f.Type == ConfigFieldType.Secret)
                {
                    if (string.IsNullOrEmpty(v)) _secrets.Remove(SecretKey(p, f));
                    else _secrets.Set(SecretKey(p, f), v);
                }
                else if (!string.IsNullOrEmpty(v))
                {
                    entry.Values[f.Key] = v;
                }
            }
            all[p.Key()] = entry;
            Write(all);
        }
    }

    private static string SecretKey(IMediaProvider p, ConfigField f) => $"{p.ProviderId}:{f.Key}";

    private Dictionary<string, Entry> Load()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path)) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private void Write(Dictionary<string, Entry> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}
