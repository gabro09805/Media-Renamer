using System.Net;
using System.Text;
using MediaRenamer.Infrastructure;
using MediaRenamer.Infrastructure.Caching;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Providers.Tests;

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public List<(Uri Uri, string? Auth)> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString()));
        return Task.FromResult(_respond(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal static class TestHost
{
    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mr-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static (IPluginContext Context, FakeHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond, string? dataDir = null)
    {
        var handler = new FakeHandler(respond);
        var ctx = new PluginContext(new MemoryResponseCache(), TimeSpan.FromHours(1), dataDir ?? TempDir(), () => handler);
        return (ctx, handler);
    }

    public static IProviderConfig Config(params (string Key, string Value)[] values) =>
        new DictionaryProviderConfig(values.ToDictionary(v => v.Key, v => v.Value));

    public static IProviderConfig TmdbConfig(string token = "test-token", string language = "en-US") =>
        Config(("accessToken", token), ("language", language));
}
