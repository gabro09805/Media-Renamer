using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Providers.AniDb;

public sealed class AniDbPlugin : IProviderPlugin
{
    public string Id => "anidb";
    public string Name => "AniDB";
    public Version Version => typeof(AniDbPlugin).Assembly.GetName().Version ?? new Version(1, 0);
    public int ApiVersion => PluginApi.Version;

    public IEnumerable<IMediaProvider> CreateProviders(IPluginContext context)
    {
        // AniDB: at most one request every 2 seconds, otherwise the client gets banned.
        var http = context.CreateHttpClient(Id, new HttpOptions(MinInterval: TimeSpan.FromSeconds(2.2), MaxRetries: 1));
        var client = new AniDbClient(http, context.Cache, context.CacheTtl);
        var index = new AniDbTitleIndex(http, context.GetDataDirectory(Id));
        yield return new AniDbEpisodeProvider(client, index);
    }
}
