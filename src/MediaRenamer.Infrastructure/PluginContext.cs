using System.Net;
using System.Net.Http.Headers;
using MediaRenamer.Infrastructure.Http;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Infrastructure;

/// <summary>Host services handed to plugins.</summary>
public sealed class PluginContext : IPluginContext
{
    private readonly string _dataRoot;
    private readonly Func<HttpMessageHandler> _innerHandler;

    /// <param name="innerHandler">Transport factory; tests inject a fake one.</param>
    public PluginContext(IResponseCache cache, TimeSpan cacheTtl, string dataRoot, Func<HttpMessageHandler>? innerHandler = null)
    {
        Cache = cache;
        CacheTtl = cacheTtl;
        _dataRoot = dataRoot;
        _innerHandler = innerHandler ?? (() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        });
    }

    public IResponseCache Cache { get; }
    public TimeSpan CacheTtl { get; }

    public HttpClient CreateHttpClient(string providerId, HttpOptions? options = null)
    {
        options ??= new HttpOptions();
        var handler = new RetryingHandler(options.MinInterval, options.MaxRetries, _innerHandler());
        var client = new HttpClient(handler) { Timeout = options.Timeout ?? TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MediaRenamer", "1.0"));
        return client;
    }

    public string GetDataDirectory(string providerId)
    {
        var dir = Path.Combine(_dataRoot, providerId);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
