using System.Text;
using System.Xml.Linq;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Providers.AniDb;

/// <summary>
/// AniDB HTTP API client. AniDB bans clients that exceed ~1 request / 2 s or do not cache,
/// so the interval is enforced by the HTTP client (see plugin) and responses are cached for at least 24 h.
/// </summary>
internal sealed class AniDbClient
{
    private const string ApiUrl = "http://api.anidb.net:9001/httpapi";
    private static readonly TimeSpan MinTtl = TimeSpan.FromHours(24);
    private static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";

    private readonly HttpClient _http;
    private readonly IResponseCache _cache;
    private readonly TimeSpan _ttl;

    public AniDbClient(HttpClient http, IResponseCache cache, TimeSpan ttl)
    {
        _http = http;
        _cache = cache;
        _ttl = ttl < MinTtl ? MinTtl : ttl;
    }

    public static XName Lang => Xml + "lang";

    public async Task<XElement> GetAnimeAsync(int aid, IProviderConfig config, CancellationToken ct)
    {
        var client = config.Get(AniDbConfig.ClientName)
            ?? throw new ProviderException(ProviderErrorKind.Configuration, "AniDB client name is not configured.");
        var version = config.Get(AniDbConfig.ClientVersion) ?? "1";

        var url = $"{ApiUrl}?client={Uri.EscapeDataString(client)}&clientver={Uri.EscapeDataString(version)}&protover=1&request=anime&aid={aid}";
        var cacheKey = $"anidb:anime:{aid}";

        var cached = await _cache.GetAsync(cacheKey, ct);
        if (cached is not null) return Parse(cached);

        byte[] bytes;
        try
        {
            using var response = await _http.GetAsync(url, ct);
            if ((int)response.StatusCode == 429)
                throw new ProviderException(ProviderErrorKind.RateLimited, "AniDB rate limit exceeded.");
            if (!response.IsSuccessStatusCode)
                throw new ProviderException(ProviderErrorKind.Network, $"AniDB returned HTTP {(int)response.StatusCode}.");
            bytes = AniDbTitleIndex.Decompress(await response.Content.ReadAsByteArrayAsync(ct));
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(ProviderErrorKind.Network, "Cannot reach AniDB: " + ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ProviderException(ProviderErrorKind.Network, "AniDB request timed out.", ex);
        }

        var text = Encoding.UTF8.GetString(bytes);
        var root = Parse(text);
        await _cache.SetAsync(cacheKey, text, _ttl, ct);
        return root;
    }

    private static XElement Parse(string text)
    {
        XElement root;
        try
        {
            root = XDocument.Parse(text).Root ?? throw new System.Xml.XmlException("empty");
        }
        catch (System.Xml.XmlException ex)
        {
            throw new ProviderException(ProviderErrorKind.InvalidResponse, "AniDB returned an invalid response.", ex);
        }

        if (root.Name.LocalName == "error")
        {
            var msg = root.Value.Trim();
            var banned = msg.Contains("banned", StringComparison.OrdinalIgnoreCase);
            throw new ProviderException(banned ? ProviderErrorKind.Banned : ProviderErrorKind.InvalidResponse, "AniDB: " + msg);
        }
        return root;
    }
}
