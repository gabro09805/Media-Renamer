using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MediaRenamer.Platform;

/// <summary>Secrets protected with Windows DPAPI (current user). Values are not portable between users or machines.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MediaRenamer.Secrets.v1");
    private readonly string _path;
    private readonly object _gate = new();

    public DpapiSecretStore(string path) => _path = path;

    public string? Get(string key)
    {
        lock (_gate)
        {
            if (!Load().TryGetValue(key, out var b64)) return null;
            try
            {
                var plain = ProtectedData.Unprotect(Convert.FromBase64String(b64), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (CryptographicException)
            {
                return null; // protected by another user/machine
            }
        }
    }

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            var data = Load();
            var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
            data[key] = Convert.ToBase64String(cipher);
            Save(data);
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            var data = Load();
            if (data.Remove(key)) Save(data);
        }
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private void Save(Dictionary<string, string> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data));
        File.Move(tmp, _path, overwrite: true);
    }
}

/// <summary>Non-persistent store, for tests.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _data = new();

    public string? Get(string key) => _data.TryGetValue(key, out var v) ? v : null;
    public void Set(string key, string value) => _data[key] = value;
    public void Remove(string key) => _data.Remove(key);
}
