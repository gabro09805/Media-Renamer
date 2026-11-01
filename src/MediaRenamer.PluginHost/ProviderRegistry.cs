using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.PluginHost;

public sealed class ProviderEntry
{
    public ProviderEntry(IProviderPlugin plugin, IMediaProvider provider)
    {
        Plugin = plugin;
        Provider = provider;
    }

    public IProviderPlugin Plugin { get; }
    public IMediaProvider Provider { get; }
    public string Key => Provider.Key();
    public string Label => Provider.Label();
    public bool IsEnabled { get; internal set; } = true;
}

public sealed class PluginIncompatibleException : Exception
{
    public PluginIncompatibleException(string message) : base(message) { }
}

/// <summary>
/// Holds every registered (provider, mode) pair. There is deliberately no priority or fallback logic:
/// the user always picks the entry to use.
/// </summary>
public sealed class ProviderRegistry
{
    private readonly List<ProviderEntry> _entries = new();

    public event Action? Changed;

    public IReadOnlyList<ProviderEntry> All => _entries;

    public IEnumerable<ProviderEntry> Enabled(ProviderMode mode) =>
        _entries.Where(e => e.IsEnabled && e.Provider.Mode == mode);

    public ProviderEntry? Find(string providerId, ProviderMode mode) =>
        _entries.FirstOrDefault(e => e.Provider.ProviderId == providerId && e.Provider.Mode == mode);

    /// <summary>Registers every provider of a plugin, all or nothing.</summary>
    public IReadOnlyList<ProviderEntry> Register(IProviderPlugin plugin, IPluginContext context)
    {
        if (plugin.ApiVersion != PluginApi.Version)
            throw new PluginIncompatibleException(
                $"Plugin '{plugin.Id}' targets plugin API v{plugin.ApiVersion}, but the application provides v{PluginApi.Version}.");

        var created = plugin.CreateProviders(context).Select(p => new ProviderEntry(plugin, p)).ToList();

        var seen = new HashSet<string>(_entries.Select(e => e.Key));
        foreach (var e in created)
            if (!seen.Add(e.Key))
                throw new InvalidOperationException($"A provider with key '{e.Key}' is already registered.");

        _entries.AddRange(created);
        Changed?.Invoke();
        return created;
    }

    public void Unregister(string pluginId)
    {
        if (_entries.RemoveAll(e => e.Plugin.Id == pluginId) > 0) Changed?.Invoke();
    }

    public void SetEnabled(ProviderEntry entry, bool enabled)
    {
        if (entry.IsEnabled == enabled) return;
        entry.IsEnabled = enabled;
        Changed?.Invoke();
    }
}
