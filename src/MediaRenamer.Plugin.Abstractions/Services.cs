namespace MediaRenamer.Plugin.Abstractions;

/// <summary>Text cache shared by all providers; keys must include provider, mode, language and endpoint.</summary>
public interface IResponseCache
{
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct);
}

/// <summary>HTTP behaviour requested by a provider: throttling, timeout and retry on 429/5xx.</summary>
public sealed record HttpOptions(TimeSpan? MinInterval = null, TimeSpan? Timeout = null, int MaxRetries = 3);

/// <summary>Services the host gives to plugins, so plugins never depend on the application core.</summary>
public interface IPluginContext
{
    /// <summary>Client with throttling, retry and timeout applied. Only GET requests are retried.</summary>
    HttpClient CreateHttpClient(string providerId, HttpOptions? options = null);
    IResponseCache Cache { get; }
    TimeSpan CacheTtl { get; }
    string GetDataDirectory(string providerId);
}

public enum ProviderErrorKind { Configuration, Authentication, RateLimited, Banned, NotFound, Network, InvalidResponse }

/// <summary>Error surfaced to the user as-is. The host never falls back to another provider.</summary>
public sealed class ProviderException : Exception
{
    public ProviderErrorKind Kind { get; }

    public ProviderException(ProviderErrorKind kind, string message, Exception? inner = null) : base(message, inner) =>
        Kind = kind;
}

public static class MediaProviderExtensions
{
    /// <summary>Stable key, e.g. "tmdb.movie". Used for settings and registry lookups.</summary>
    public static string Key(this IMediaProvider p) => $"{p.ProviderId}.{p.Mode.ToString().ToLowerInvariant()}";

    /// <summary>Menu label, e.g. "TMDB – Movie Mode".</summary>
    public static string Label(this IMediaProvider p) => $"{p.DisplayName} – {p.Mode} Mode";
}

/// <summary>Simple dictionary-backed configuration.</summary>
public sealed class DictionaryProviderConfig : IProviderConfig
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public DictionaryProviderConfig(IReadOnlyDictionary<string, string> values) => _values = values;

    public string? Get(string key) => _values.TryGetValue(key, out var v) && v.Length > 0 ? v : null;
}
