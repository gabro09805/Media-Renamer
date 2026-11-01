using MediaRenamer.Bindings.Formats;
using MediaRenamer.Infrastructure;
using MediaRenamer.Infrastructure.Caching;
using MediaRenamer.Infrastructure.Settings;
using MediaRenamer.Plugin.Abstractions;
using MediaRenamer.Platform;
using MediaRenamer.PluginHost;
using MediaRenamer.Providers.AniDb;
using MediaRenamer.Providers.Tmdb;

namespace MediaRenamer.App.Services;

/// <summary>Composition root: wires paths, secrets, cache, plugin host and provider settings.</summary>
public sealed class AppServices
{
    private readonly FileResponseCache _cache;

    private AppServices(AppPaths paths, ProviderRegistry registry, ProviderConfigService settings,
        MatchPreferencesStore matchPreferences, FormatStore formats, FileResponseCache cache, IReadOnlyList<PluginLoadResult> pluginResults,
        AppSettingsStore appSettings)
    {
        AppSettings = appSettings;
        Formats = formats;
        Paths = paths;
        Registry = registry;
        Settings = settings;
        MatchPreferences = matchPreferences;
        _cache = cache;
        PluginResults = pluginResults;
    }

    public AppPaths Paths { get; }
    public ProviderRegistry Registry { get; }
    public ProviderConfigService Settings { get; }
    public MatchPreferencesStore MatchPreferences { get; }
    public FormatStore Formats { get; }
    public AppSettingsStore AppSettings { get; }
    public FormatRenderer Renderer { get; } = new();
    public IReadOnlyList<PluginLoadResult> PluginResults { get; }

    public void ClearCache() => _cache.Clear();

    public static AppServices Create(AppPaths? paths = null, ISecretStore? secrets = null, IPluginContext? context = null)
    {
        paths ??= AppPaths.Resolve();
        paths.EnsureCreated();

        // DPAPI is Windows only; elsewhere secrets stay in memory until a libsecret store exists.
        secrets ??= OperatingSystem.IsWindows()
            ? new DpapiSecretStore(Path.Combine(paths.Config, "secrets.dat"))
            : new InMemorySecretStore();

        var cache = new FileResponseCache(paths.Cache);
        context ??= new PluginContext(cache, TimeSpan.FromHours(24), paths.Data);
        var settings = new ProviderConfigService(Path.Combine(paths.Config, "providers.json"), secrets);

        var registry = new ProviderRegistry();
        registry.Register(new TmdbPlugin(), context);
        registry.Register(new AniDbPlugin(), context);
        var results = new PluginLoader().LoadAll(paths.Plugins, registry, context);

        foreach (var entry in registry.All)
            registry.SetEnabled(entry, settings.IsEnabled(entry.Provider));

        var matchPreferences = new MatchPreferencesStore(Path.Combine(paths.Config, "match-preferences.json"));

        var formats = new FormatStore(Path.Combine(paths.Config, "formats.json"));

        return new AppServices(paths, registry, settings, matchPreferences, formats, cache, results,
            new AppSettingsStore(Path.Combine(paths.Config, "app-settings.json")));
    }
}
