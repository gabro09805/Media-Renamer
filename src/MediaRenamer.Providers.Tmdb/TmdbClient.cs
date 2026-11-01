using System.Net;
using System.Text.Json;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Providers.Tmdb;

/// <summary>Shared HTTP/auth/cache layer. Movie and Episode modes are separate providers built on top of it.</summary>
internal sealed class TmdbClient
{
    private const string BaseUrl = "https://api.themoviedb.org/3/";

    private readonly HttpClient _http;
    private readonly IResponseCache _cache;
    private readonly TimeSpan _ttl;

    public TmdbClient(HttpClient http, IResponseCache cache, TimeSpan ttl)
    {
        _http = http;
        _cache = cache;
        _ttl = ttl;
    }

    public async Task<JsonElement> GetAsync(string path, IReadOnlyDictionary<string, string?> query, IProviderConfig config, CancellationToken ct)
    {
        var token = config.Get(TmdbConfig.AccessToken)
            ?? throw new ProviderException(ProviderErrorKind.Configuration, "TMDB access token is not configured.");

        var url = BaseUrl + path + BuildQuery(query);
        var cacheKey = "tmdb:" + url;

        var cached = await _cache.GetAsync(cacheKey, ct);
        if (cached is not null) return Parse(cached);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Accept.Add(new("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(ProviderErrorKind.Network, "Cannot reach TMDB: " + ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ProviderException(ProviderErrorKind.Network, "TMDB request timed out.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    throw new ProviderException(ProviderErrorKind.Authentication, "TMDB rejected the access token (401).");
                case HttpStatusCode.NotFound:
                    throw new ProviderException(ProviderErrorKind.NotFound, "TMDB resource not found (404).");
                case HttpStatusCode.TooManyRequests:
                    throw new ProviderException(ProviderErrorKind.RateLimited, "TMDB rate limit exceeded (429). Try again shortly.");
            }
            if (!response.IsSuccessStatusCode)
                throw new ProviderException(ProviderErrorKind.Network, $"TMDB returned HTTP {(int)response.StatusCode}.");

            var json = Parse(body);
            await _cache.SetAsync(cacheKey, body, _ttl, ct);
            return json;
        }
    }

    public async Task<ConnectionTestResult> TestAsync(IProviderConfig config, CancellationToken ct)
    {
        try
        {
            await GetAsync("configuration", new Dictionary<string, string?>(), config, ct);
            return new ConnectionTestResult(true, "Connected to TMDB.");
        }
        catch (ProviderException ex)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
    }

    private static JsonElement Parse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ProviderException(ProviderErrorKind.InvalidResponse, "TMDB returned an invalid response.", ex);
        }
    }

    private static string BuildQuery(IReadOnlyDictionary<string, string?> query)
    {
        var parts = query.Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");
        var s = string.Join("&", parts);
        return s.Length == 0 ? "" : "?" + s;
    }
}

internal static class TmdbConfig
{
    public const string AccessToken = "accessToken";

    /// <summary>Used only when the caller gives no language; the output language is chosen in Match Preferences.</summary>
    public const string DefaultLanguage = "en-US";

    public static readonly IReadOnlyList<ConfigField> Schema = new[]
    {
        new ConfigField(AccessToken, "API Read Access Token", ConfigFieldType.Secret, Required: true),
    };
}
