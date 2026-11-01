using System.Text.Json;
using MediaRenamer.Infrastructure;
using MediaRenamer.Infrastructure.Caching;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;
using MediaRenamer.PluginHost;
using MediaRenamer.Providers.Tmdb;

namespace MediaRenamer.PluginHost.Tests;

public class ProviderRegistryTests
{
    private sealed class StubProvider : IMediaProvider
    {
        public StubProvider(string id, ProviderMode mode) { ProviderId = id; Mode = mode; }
        public string ProviderId { get; }
        public string DisplayName => ProviderId.ToUpperInvariant();
        public ProviderMode Mode { get; }
        public ProviderCapabilities Capabilities => default;
        public IReadOnlyList<ConfigField> ConfigSchema => Array.Empty<ConfigField>();
        public Task<ConnectionTestResult> TestConnectionAsync(IProviderConfig config, CancellationToken ct) =>
            Task.FromResult(new ConnectionTestResult(true, "ok"));
    }

    private sealed class StubPlugin : IProviderPlugin
    {
        private readonly (string Id, ProviderMode Mode)[] _providers;
        public StubPlugin(string id, int apiVersion, params (string, ProviderMode)[] providers)
        {
            Id = id; ApiVersion = apiVersion; _providers = providers;
        }
        public string Id { get; }
        public string Name => Id;
        public Version Version => new(1, 0);
        public int ApiVersion { get; }
        public IEnumerable<IMediaProvider> CreateProviders(IPluginContext context) =>
            _providers.Select(p => new StubProvider(p.Id, p.Mode));
    }

    private static IPluginContext Ctx() =>
        new PluginContext(new MemoryResponseCache(), TimeSpan.FromHours(1), Path.GetTempPath());

    [Fact]
    public void Register_adds_one_entry_per_provider_and_raises_Changed()
    {
        var reg = new ProviderRegistry();
        var changes = 0;
        reg.Changed += () => changes++;
        var entries = reg.Register(new StubPlugin("a", PluginApi.Version, ("a", ProviderMode.Movie), ("a", ProviderMode.Episode)), Ctx());

        Assert.Equal(2, entries.Count);
        Assert.Equal(new[] { "a.movie", "a.episode" }, reg.All.Select(e => e.Key));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Incompatible_api_version_is_rejected_without_registering_anything()
    {
        var reg = new ProviderRegistry();
        Assert.Throws<PluginIncompatibleException>(() =>
            reg.Register(new StubPlugin("old", PluginApi.Version + 1, ("old", ProviderMode.Movie)), Ctx()));
        Assert.Empty(reg.All);
    }

    [Fact]
    public void Duplicate_key_rejects_the_whole_plugin()
    {
        var reg = new ProviderRegistry();
        reg.Register(new StubPlugin("a", PluginApi.Version, ("a", ProviderMode.Movie)), Ctx());

        Assert.Throws<InvalidOperationException>(() =>
            reg.Register(new StubPlugin("b", PluginApi.Version, ("b", ProviderMode.Movie), ("a", ProviderMode.Movie)), Ctx()));
        Assert.Single(reg.All);
    }

    [Fact]
    public void Enabled_filters_by_mode_and_enabled_flag_without_any_fallback_logic()
    {
        var reg = new ProviderRegistry();
        reg.Register(new StubPlugin("a", PluginApi.Version, ("a", ProviderMode.Movie), ("a", ProviderMode.Episode)), Ctx());
        reg.Register(new StubPlugin("b", PluginApi.Version, ("b", ProviderMode.Episode)), Ctx());

        Assert.Equal(new[] { "a.episode", "b.episode" }, reg.Enabled(ProviderMode.Episode).Select(e => e.Key));
        reg.SetEnabled(reg.Find("a", ProviderMode.Episode)!, false);
        Assert.Equal(new[] { "b.episode" }, reg.Enabled(ProviderMode.Episode).Select(e => e.Key));
        Assert.Single(reg.Enabled(ProviderMode.Movie));
    }

    [Fact]
    public void Unregister_removes_all_providers_of_a_plugin()
    {
        var reg = new ProviderRegistry();
        reg.Register(new StubPlugin("a", PluginApi.Version, ("a", ProviderMode.Movie), ("a", ProviderMode.Episode)), Ctx());
        reg.Unregister("a");
        Assert.Empty(reg.All);
        Assert.Null(reg.Find("a", ProviderMode.Movie));
    }
}

public class PluginLoaderTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "mr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static IPluginContext Ctx() =>
        new PluginContext(new MemoryResponseCache(), TimeSpan.FromHours(1), TempDir());

    private static string InstallTmdbCopy(string pluginsRoot, string manifestJson)
    {
        var folder = Path.Combine(pluginsRoot, "tmdb-copy");
        Directory.CreateDirectory(folder);
        File.Copy(typeof(TmdbPlugin).Assembly.Location, Path.Combine(folder, "MediaRenamer.Providers.Tmdb.dll"));
        File.WriteAllText(Path.Combine(folder, "plugin.json"), manifestJson);
        return folder;
    }

    [Fact]
    public void Loads_a_plugin_from_its_folder_and_registers_its_providers()
    {
        var root = TempDir();
        InstallTmdbCopy(root, JsonSerializer.Serialize(new { id = "tmdb", entry = "MediaRenamer.Providers.Tmdb.dll" }));
        var reg = new ProviderRegistry();

        var results = new PluginLoader().LoadAll(root, reg, Ctx());

        var r = Assert.Single(results);
        Assert.True(r.Success, r.Error);
        Assert.Equal(new[] { "tmdb.movie", "tmdb.episode" }, r.ProviderKeys);
        Assert.Equal(2, reg.All.Count);
        Assert.NotNull(reg.Find("tmdb", ProviderMode.Movie));
    }

    [Fact]
    public void Bad_manifest_is_reported_and_does_not_throw()
    {
        var root = TempDir();
        InstallTmdbCopy(root, "{ broken");
        var results = new PluginLoader().LoadAll(root, new ProviderRegistry(), Ctx());
        Assert.False(Assert.Single(results).Success);
    }

    [Fact]
    public void Missing_entry_assembly_is_reported()
    {
        var root = TempDir();
        InstallTmdbCopy(root, """{"id":"x","entry":"nope.dll"}""");
        var r = Assert.Single(new PluginLoader().LoadAll(root, new ProviderRegistry(), Ctx()));
        Assert.False(r.Success);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Entry_outside_the_plugin_folder_is_refused()
    {
        var root = TempDir();
        InstallTmdbCopy(root, """{"id":"x","entry":"..\\..\\evil.dll"}""");
        var r = Assert.Single(new PluginLoader().LoadAll(root, new ProviderRegistry(), Ctx()));
        Assert.False(r.Success);
        Assert.Contains("inside", r.Error);
    }

    [Fact]
    public void Missing_plugins_directory_is_not_an_error()
    {
        Assert.Empty(new PluginLoader().LoadAll(Path.Combine(TempDir(), "none"), new ProviderRegistry(), Ctx()));
    }

    [Fact]
    public void A_failing_plugin_does_not_prevent_the_others_from_loading()
    {
        var root = TempDir();
        InstallTmdbCopy(root, JsonSerializer.Serialize(new { id = "tmdb", entry = "MediaRenamer.Providers.Tmdb.dll" }));
        var broken = Path.Combine(root, "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "plugin.json"), "{ broken");

        var results = new PluginLoader().LoadAll(root, new ProviderRegistry(), Ctx());

        Assert.Equal(2, results.Count);
        Assert.Single(results, r => r.Success);
    }
}
