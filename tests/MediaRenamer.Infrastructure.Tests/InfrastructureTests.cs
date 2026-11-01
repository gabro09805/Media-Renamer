using System.Net;
using MediaRenamer.Infrastructure;
using MediaRenamer.Infrastructure.Caching;
using MediaRenamer.Infrastructure.Http;
using MediaRenamer.Infrastructure.Settings;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;
using MediaRenamer.Platform;

namespace MediaRenamer.Infrastructure.Tests;

internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _script;
    public int Calls { get; private set; }

    public ScriptedHandler(params Func<HttpResponseMessage>[] script) => _script = new(script);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(_script.Count > 1 ? _script.Dequeue()() : _script.Peek()());
    }
}

public class RetryingHandlerTests
{
    private static HttpResponseMessage Status(HttpStatusCode c, TimeSpan? retryAfter = null)
    {
        var r = new HttpResponseMessage(c);
        if (retryAfter is { } ra) r.Headers.RetryAfter = new(ra);
        return r;
    }

    private static (HttpClient Client, List<TimeSpan> Delays) Client(ScriptedHandler inner, int retries, TimeSpan? interval = null)
    {
        var delays = new List<TimeSpan>();
        var h = new RetryingHandler(interval, retries, inner, (d, _) => { delays.Add(d); return Task.CompletedTask; });
        return (new HttpClient(h), delays);
    }

    [Fact]
    public async Task Retries_5xx_then_succeeds()
    {
        var inner = new ScriptedHandler(() => Status(HttpStatusCode.ServiceUnavailable), () => Status(HttpStatusCode.OK));
        var (c, delays) = Client(inner, 3);
        var r = await c.GetAsync("http://x/");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(2, inner.Calls);
        Assert.Single(delays);
    }

    [Fact]
    public async Task Honours_Retry_After_on_429()
    {
        var inner = new ScriptedHandler(() => Status(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(7)), () => Status(HttpStatusCode.OK));
        var (c, delays) = Client(inner, 3);
        await c.GetAsync("http://x/");
        Assert.Equal(TimeSpan.FromSeconds(7), delays[0]);
    }

    [Fact]
    public async Task Gives_up_after_max_retries_and_returns_last_response()
    {
        var inner = new ScriptedHandler(() => Status(HttpStatusCode.TooManyRequests, TimeSpan.Zero));
        var (c, _) = Client(inner, 2);
        var r = await c.GetAsync("http://x/");
        Assert.Equal(HttpStatusCode.TooManyRequests, r.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Does_not_retry_client_errors(HttpStatusCode code)
    {
        var inner = new ScriptedHandler(() => Status(code));
        var (c, _) = Client(inner, 3);
        Assert.Equal(code, (await c.GetAsync("http://x/")).StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Network_errors_are_retried_and_finally_rethrown()
    {
        var inner = new ScriptedHandler(() => throw new HttpRequestException("down"));
        var (c, _) = Client(inner, 1);
        await Assert.ThrowsAsync<HttpRequestException>(() => c.GetAsync("http://x/"));
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Requests_are_spaced_by_the_minimum_interval()
    {
        var inner = new ScriptedHandler(() => Status(HttpStatusCode.OK));
        var (c, delays) = Client(inner, 0, TimeSpan.FromSeconds(2));
        await c.GetAsync("http://x/");
        await c.GetAsync("http://x/");
        Assert.Single(delays);
        Assert.InRange(delays[0], TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Requests_with_a_body_are_rejected()
    {
        var (c, _) = Client(new ScriptedHandler(() => Status(HttpStatusCode.OK)), 1);
        await Assert.ThrowsAsync<NotSupportedException>(() => c.PostAsync("http://x/", new StringContent("a")));
    }
}

public class CacheTests
{
    private static string Temp() => Path.Combine(Path.GetTempPath(), "mr-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Memory_cache_returns_value_until_expiry()
    {
        var c = new MemoryResponseCache();
        await c.SetAsync("k", "v", TimeSpan.FromMinutes(1), default);
        Assert.Equal("v", await c.GetAsync("k", default));
        await c.SetAsync("old", "v", TimeSpan.FromMilliseconds(-1), default);
        Assert.Null(await c.GetAsync("old", default));
        Assert.Null(await c.GetAsync("missing", default));
    }

    [Fact]
    public async Task File_cache_roundtrips_multiline_values_and_survives_new_instances()
    {
        var dir = Temp();
        const string value = "{\n  \"a\": 1,\n  \"b\": \"è\"\n}";
        await new FileResponseCache(dir).SetAsync("tmdb.movie|238|it-IT", value, TimeSpan.FromHours(1), default);
        Assert.Equal(value, await new FileResponseCache(dir).GetAsync("tmdb.movie|238|it-IT", default));
        Assert.Null(await new FileResponseCache(dir).GetAsync("other", default));
    }

    [Fact]
    public async Task File_cache_ignores_expired_and_corrupt_entries_and_can_be_cleared()
    {
        var dir = Temp();
        var c = new FileResponseCache(dir);
        await c.SetAsync("expired", "v", TimeSpan.FromMilliseconds(-1), default);
        Assert.Null(await c.GetAsync("expired", default));

        await c.SetAsync("k", "v", TimeSpan.FromHours(1), default);
        foreach (var f in Directory.GetFiles(dir)) File.WriteAllText(f, "garbage without newline");
        Assert.Null(await c.GetAsync("k", default));

        await c.SetAsync("k", "v", TimeSpan.FromHours(1), default);
        c.Clear();
        Assert.Empty(Directory.GetFiles(dir));
    }
}

public class SecretStoreTests
{
    [Fact]
    public void InMemory_store_basic_operations()
    {
        var s = new InMemorySecretStore();
        s.Set("a", "1");
        Assert.Equal("1", s.Get("a"));
        s.Remove("a");
        Assert.Null(s.Get("a"));
    }

    [SkippableFact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Dpapi_store_encrypts_on_disk_and_roundtrips()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows only.");
        var path = Path.Combine(Path.GetTempPath(), "mr-tests", Guid.NewGuid().ToString("N"), "secrets.json");
        var store = new DpapiSecretStore(path);
        store.Set("tmdb:accessToken", "super-secret-value");

        Assert.DoesNotContain("super-secret-value", File.ReadAllText(path));
        Assert.Equal("super-secret-value", new DpapiSecretStore(path).Get("tmdb:accessToken"));

        store.Remove("tmdb:accessToken");
        Assert.Null(store.Get("tmdb:accessToken"));
    }
}

public class ProviderConfigServiceTests
{
    private sealed class FakeProvider : IMediaProvider
    {
        public FakeProvider(ProviderMode mode) => Mode = mode;
        public string ProviderId => "fake";
        public string DisplayName => "Fake";
        public ProviderMode Mode { get; }
        public ProviderCapabilities Capabilities => default;
        public IReadOnlyList<ConfigField> ConfigSchema { get; } = new[]
        {
            new ConfigField("token", "Token", ConfigFieldType.Secret, Required: true),
            new ConfigField("language", "Language", ConfigFieldType.Language, DefaultValue: "en-US"),
        };
        public Task<ConnectionTestResult> TestConnectionAsync(IProviderConfig config, CancellationToken ct) =>
            Task.FromResult(new ConnectionTestResult(true, "ok"));
    }

    private static (ProviderConfigService Svc, string Path, InMemorySecretStore Secrets) Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "mr-tests", Guid.NewGuid().ToString("N"), "providers.json");
        var secrets = new InMemorySecretStore();
        return (new ProviderConfigService(path, secrets), path, secrets);
    }

    [Fact]
    public void Defaults_apply_when_nothing_is_saved()
    {
        var (svc, _, _) = Create();
        var p = new FakeProvider(ProviderMode.Movie);
        Assert.True(svc.IsEnabled(p));
        var v = svc.GetValues(p);
        Assert.Equal("en-US", v["language"]);
        Assert.False(v.ContainsKey("token"));
    }

    [Fact]
    public void Secrets_go_to_the_secret_store_never_to_the_json_file()
    {
        var (svc, path, secrets) = Create();
        var p = new FakeProvider(ProviderMode.Movie);
        svc.Save(p, true, new Dictionary<string, string> { ["token"] = "s3cret", ["language"] = "it-IT" });

        Assert.DoesNotContain("s3cret", File.ReadAllText(path));
        Assert.Equal("s3cret", secrets.Get("fake:token"));
        Assert.Equal("it-IT", svc.GetConfig(p).Get("language"));
        Assert.Equal("s3cret", svc.GetConfig(p).Get("token"));
    }

    [Fact]
    public void Credentials_are_shared_between_modes_but_other_options_and_enabled_flag_are_per_mode()
    {
        var (svc, _, _) = Create();
        var movie = new FakeProvider(ProviderMode.Movie);
        var episode = new FakeProvider(ProviderMode.Episode);

        svc.Save(movie, true, new Dictionary<string, string> { ["token"] = "t", ["language"] = "it-IT" });
        svc.Save(episode, false, new Dictionary<string, string> { ["token"] = "t", ["language"] = "fr-FR" });

        Assert.Equal("t", svc.GetConfig(episode).Get("token"));
        Assert.Equal("it-IT", svc.GetConfig(movie).Get("language"));
        Assert.Equal("fr-FR", svc.GetConfig(episode).Get("language"));
        Assert.True(svc.IsEnabled(movie));
        Assert.False(svc.IsEnabled(episode));
    }

    [Fact]
    public void Saving_an_empty_secret_removes_it_and_settings_persist_across_instances()
    {
        var (svc, path, secrets) = Create();
        var p = new FakeProvider(ProviderMode.Movie);
        svc.Save(p, true, new Dictionary<string, string> { ["token"] = "t" });
        svc.Save(p, false, new Dictionary<string, string> { ["token"] = "" });

        Assert.Null(secrets.Get("fake:token"));
        Assert.False(new ProviderConfigService(path, secrets).IsEnabled(p));
    }

    [Fact]
    public void Corrupt_settings_file_falls_back_to_defaults()
    {
        var (svc, path, _) = Create();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Assert.True(svc.IsEnabled(new FakeProvider(ProviderMode.Movie)));
    }
}

public class PluginContextAndPathsTests
{
    [Fact]
    public void Data_directories_are_separate_per_provider_and_created_on_demand()
    {
        var root = Path.Combine(Path.GetTempPath(), "mr-tests", Guid.NewGuid().ToString("N"));
        var ctx = new PluginContext(new MemoryResponseCache(), TimeSpan.FromHours(1), root);
        var a = ctx.GetDataDirectory("a");
        var b = ctx.GetDataDirectory("b");
        Assert.NotEqual(a, b);
        Assert.True(Directory.Exists(a));
    }

    [Fact]
    public async Task Http_client_sends_a_user_agent_through_the_injected_transport()
    {
        var inner = new ScriptedHandler(() => new HttpResponseMessage(HttpStatusCode.OK));
        var ctx = new PluginContext(new MemoryResponseCache(), TimeSpan.FromHours(1), Path.GetTempPath(), () => inner);
        var client = ctx.CreateHttpClient("x");
        Assert.Contains(client.DefaultRequestHeaders.UserAgent, p => p.Product?.Name == "MediaRenamer");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("http://x/")).StatusCode);
    }

    [Fact]
    public void AppPaths_layout()
    {
        var p = new AppPaths(@"C:\root");
        Assert.Equal(Path.Combine(@"C:\root", "plugins"), p.Plugins);
        Assert.Equal(Path.Combine(@"C:\root", "cache"), p.Cache);
    }
}
