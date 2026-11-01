using System.IO.Compression;
using System.Net;
using System.Text;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;
using MediaRenamer.Providers.AniDb;

namespace MediaRenamer.Providers.Tests;

public class AniDbEpisodeModeTests
{
    private const string DumpXml = """
    <?xml version="1.0" encoding="UTF-8"?>
    <animetitles>
      <anime aid="266">
        <title xml:lang="x-jat" type="main">Meitantei Conan</title>
        <title xml:lang="en" type="official">Detective Conan</title>
        <title xml:lang="en" type="synonym">Case Closed</title>
        <title xml:lang="it" type="official">Detective Conan</title>
      </anime>
      <anime aid="2"><title xml:lang="x-jat" type="main">Café Soleil</title></anime>
      <anime aid="3"><title xml:lang="x-jat" type="main">Conan the Boy in Future</title></anime>
    </animetitles>
    """;

    private const string AnimeXml = """
    <anime id="266" restricted="false">
      <type>TV Series</type>
      <startdate>1996-01-08</startdate>
      <titles>
        <title xml:lang="x-jat" type="main">Meitantei Conan</title>
        <title xml:lang="en" type="official">Detective Conan</title>
        <title xml:lang="it" type="official">Detective Conan IT</title>
        <title xml:lang="en" type="synonym">Case Closed</title>
      </titles>
      <description>Un detective.</description>
      <picture>266.jpg</picture>
      <episodes>
        <episode id="100"><epno type="1">1</epno><length>25</length><airdate>1996-01-08</airdate>
          <title xml:lang="en">The Roller Coaster Murder Case</title><title xml:lang="it">Il caso delle montagne russe</title></episode>
        <episode id="101"><epno type="1">2</epno><title xml:lang="en">The Stranded Murder Case</title></episode>
        <episode id="900"><epno type="2">S1</epno><title xml:lang="en">Special One</title></episode>
        <episode id="901"><epno type="3">C1</epno><title xml:lang="en">Opening</title></episode>
      </episodes>
    </anime>
    """;

    private static byte[] Gzip(string text)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionMode.Compress)) gz.Write(Encoding.UTF8.GetBytes(text));
        return ms.ToArray();
    }

    private static IEpisodeProvider Provider(Func<HttpRequestMessage, HttpResponseMessage> respond, out FakeHandler handler, string? dataDir = null)
    {
        var (ctx, h) = TestHost.Create(respond, dataDir);
        handler = h;
        return new AniDbPlugin().CreateProviders(ctx).OfType<IEpisodeProvider>().Single();
    }

    private static HttpResponseMessage Route(HttpRequestMessage r) =>
        r.RequestUri!.Host == "anidb.net"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Gzip(DumpXml)) }
            : FakeHandler.Json(AnimeXml);

    private static readonly IProviderConfig Config = TestHost.Config(("clientName", "myclient"), ("clientVersion", "3"));

    [Fact]
    public async Task Search_uses_the_title_dump_and_matches_any_language_or_synonym()
    {
        var p = Provider(Route, out var h);

        var byEnglish = await p.SearchSeriesAsync(new SearchQuery("detective conan"), Config, default);
        Assert.Equal("266", byEnglish[0].Ref.Id);

        var bySynonym = await p.SearchSeriesAsync(new SearchQuery("case closed"), Config, default);
        Assert.Equal("266", bySynonym[0].Ref.Id);
        Assert.Equal("Anime", bySynonym[0].Kind);

        var byRomaji = await p.SearchSeriesAsync(new SearchQuery("Meitantei Conan"), Config, default);
        Assert.Equal("266", byRomaji[0].Ref.Id);

        Assert.Single(h.Requests); // the dump is downloaded once
    }

    [Fact]
    public async Task Search_ignores_diacritics_and_ranks_exact_matches_first()
    {
        var p = Provider(Route, out _);
        var cafe = await p.SearchSeriesAsync(new SearchQuery("cafe soleil"), Config, default);
        Assert.Equal("2", cafe[0].Ref.Id);

        var conan = await p.SearchSeriesAsync(new SearchQuery("conan"), Config, default);
        Assert.Equal(2, conan.Count);
        Assert.Equal(new[] { "266", "3" }, conan.Select(c => c.Ref.Id).OrderByDescending(x => x == "266").ThenBy(x => x));
    }

    [Fact]
    public async Task A_fresh_local_dump_is_used_without_downloading()
    {
        var dir = TestHost.TempDir();
        var data = Path.Combine(dir, "anidb");
        Directory.CreateDirectory(data);
        await File.WriteAllTextAsync(Path.Combine(data, "anime-titles.xml"), DumpXml);

        var p = Provider(_ => throw new InvalidOperationException("must not download"), out var h, dir);
        var r = await p.SearchSeriesAsync(new SearchQuery("detective conan"), Config, default);

        Assert.Equal("266", r[0].Ref.Id);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Dump_download_failure_without_local_copy_is_reported()
    {
        var p = Provider(_ => FakeHandler.Json("x", HttpStatusCode.Forbidden), out _);
        var ex = await Assert.ThrowsAsync<ProviderException>(() => p.SearchSeriesAsync(new SearchQuery("x"), Config, default));
        Assert.Equal(ProviderErrorKind.Network, ex.Kind);
    }

    [Fact]
    public async Task GetSeries_maps_titles_type_and_seasons()
    {
        var p = Provider(Route, out var h);
        var s = await p.GetSeriesAsync(new ProviderRef("anidb", ProviderMode.Episode, "266"), "it", Config, default);

        Assert.Equal(SeriesType.Anime, s.Type);
        Assert.Equal(1996, s.Year);
        Assert.Equal("266", s.Ids.Get("anidb"));
        Assert.Equal(new[] { 0, 1 }, s.Seasons.Select(x => x.Number).OrderBy(x => x));
        Assert.Equal("Detective Conan IT", s.Titles.Resolve("it"));
        Assert.Equal("Detective Conan", s.Titles.Resolve("en"));
        Assert.Equal("Meitantei Conan", s.Titles.Resolve("fr", MissingTranslationBehavior.UseOriginal));
        Assert.Contains("Case Closed", s.Titles.AllForMatching());
        Assert.Equal("https://cdn.anidb.net/images/main/266.jpg", s.PosterUri!.ToString());

        var url = Assert.Single(h.Requests).Uri.Query;
        Assert.Contains("client=myclient", url);
        Assert.Contains("clientver=3", url);
        Assert.Contains("aid=266", url);
    }

    [Fact]
    public async Task GetEpisodes_maps_regular_with_absolute_and_specials_and_skips_other_types()
    {
        var p = Provider(Route, out _);
        var eps = await p.GetEpisodesAsync(new ProviderRef("anidb", ProviderMode.Episode, "266"), new[] { "it" }, Config, default);

        Assert.Equal(3, eps.Count);
        Assert.Equal(new Numbering(1, 1, 1), eps[0].Number);
        Assert.Equal(new Numbering(1, 2, 2), eps[1].Number);
        Assert.Equal(new Numbering(0, 1, null, 1), eps[2].Number);
        Assert.True(eps[2].IsSpecial);

        Assert.Equal("Il caso delle montagne russe", eps[0].Titles.Resolve("it"));
        Assert.Equal("The Roller Coaster Murder Case", eps[0].Titles.Resolve("en"));
        Assert.Equal(new DateOnly(1996, 1, 8), eps[0].AirDate);
        Assert.Equal(25, eps[0].RuntimeMinutes);
        Assert.Equal("100", eps[0].Source.Id);
    }

    [Fact]
    public async Task Responses_are_cached_to_protect_against_bans()
    {
        var p = Provider(Route, out var h);
        var id = new ProviderRef("anidb", ProviderMode.Episode, "266");
        await p.GetSeriesAsync(id, "en", Config, default);
        await p.GetEpisodesAsync(id, new[] { "en" }, Config, default);
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task Missing_client_name_is_a_configuration_error_and_makes_no_request()
    {
        var p = Provider(Route, out var h);
        var ex = await Assert.ThrowsAsync<ProviderException>(() =>
            p.GetSeriesAsync(new ProviderRef("anidb", ProviderMode.Episode, "266"), "en", TestHost.Config(), default));
        Assert.Equal(ProviderErrorKind.Configuration, ex.Kind);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Ban_response_is_reported_as_banned()
    {
        var p = Provider(_ => FakeHandler.Json("<error>Banned</error>"), out _);
        var ex = await Assert.ThrowsAsync<ProviderException>(() =>
            p.GetSeriesAsync(new ProviderRef("anidb", ProviderMode.Episode, "1"), "en", Config, default));
        Assert.Equal(ProviderErrorKind.Banned, ex.Kind);
    }

    [Fact]
    public async Task Gzip_api_responses_are_decompressed()
    {
        var p = Provider(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Gzip(AnimeXml)) }, out _);
        var eps = await p.GetEpisodesAsync(new ProviderRef("anidb", ProviderMode.Episode, "266"), Array.Empty<string>(), Config, default);
        Assert.Equal(3, eps.Count);
    }

    [Fact]
    public void Provider_is_episode_mode_only_and_declares_absolute_numbering()
    {
        var p = Provider(Route, out _);
        Assert.Equal("anidb.episode", p.Key());
        Assert.Equal("AniDB – Episode Mode", p.Label());
        Assert.True(p.Capabilities.HasFlag(ProviderCapabilities.AbsoluteNumbering));
        Assert.False(p is IMovieProvider);
    }
}
