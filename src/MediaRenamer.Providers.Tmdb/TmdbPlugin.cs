using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Providers.Tmdb;

public sealed class TmdbPlugin : IProviderPlugin
{
    public string Id => "tmdb";
    public string Name => "TMDB";
    public Version Version => typeof(TmdbPlugin).Assembly.GetName().Version ?? new Version(1, 0);
    public int ApiVersion => PluginApi.Version;

    public IEnumerable<IMediaProvider> CreateProviders(IPluginContext context)
    {
        // ~50 req/s is allowed by TMDB; a small interval keeps bursts polite.
        var http = context.CreateHttpClient(Id, new HttpOptions(MinInterval: TimeSpan.FromMilliseconds(30)));
        var client = new TmdbClient(http, context.Cache, context.CacheTtl);
        yield return new TmdbMovieProvider(client);
        yield return new TmdbEpisodeProvider(client);
    }
}
