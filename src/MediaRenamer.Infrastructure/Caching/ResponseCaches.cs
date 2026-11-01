using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Infrastructure.Caching;

public sealed class MemoryResponseCache : IResponseCache
{
    private readonly Dictionary<string, (DateTime Expires, string Value)> _items = new();

    public Task<string?> GetAsync(string key, CancellationToken ct)
    {
        lock (_items)
        {
            if (_items.TryGetValue(key, out var e) && e.Expires > DateTime.UtcNow) return Task.FromResult<string?>(e.Value);
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct)
    {
        lock (_items) _items[key] = (DateTime.UtcNow + ttl, value);
        return Task.CompletedTask;
    }
}

/// <summary>One file per key (SHA-256 of the key). First line is the UTC expiry, the rest is the value.</summary>
public sealed class FileResponseCache : IResponseCache
{
    private readonly string _dir;

    public FileResponseCache(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(_dir);
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return null;
        try
        {
            var text = await File.ReadAllTextAsync(path, ct);
            var nl = text.IndexOf('\n');
            if (nl < 0 || !DateTime.TryParse(text[..nl], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expires))
                return null;
            return expires > DateTime.UtcNow ? text[(nl + 1)..] : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct)
    {
        var path = PathFor(key);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(tmp, (DateTime.UtcNow + ttl).ToString("o", CultureInfo.InvariantCulture) + "\n" + value, ct);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Deletes every cached entry (manual clean from Preferences).</summary>
    public void Clear()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*.cache")) File.Delete(f);
    }

    private string PathFor(string key) =>
        Path.Combine(_dir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".cache");
}
