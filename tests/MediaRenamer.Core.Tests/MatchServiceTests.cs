using MediaRenamer.Core.Matching;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Core.Tests;

internal sealed class NoConfig : IProviderConfig
{
    public string? Get(string key) => null;
}

internal static class Fx
{
    public static TitleSet Titles(params string[] t) => new(t.Select((x, i) => new LocalizedText(x, "en", i == 0 ? TitleKind.Primary : TitleKind.Alias)));
}

internal sealed class FakeSeries : IEpisodeProvider
{
    public int Searches;
    public string ProviderId => "fake";
    public string DisplayName => "Fake";
    public ProviderMode Mode => ProviderMode.Episode;
    public ProviderCapabilities Capabilities => ProviderCapabilities.None;
    public IReadOnlyList<ConfigField> ConfigSchema => Array.Empty<ConfigField>();
    public Task<ConnectionTestResult> TestConnectionAsync(IProviderConfig c, CancellationToken ct) => Task.FromResult(new ConnectionTestResult(true, "ok"));
    private static readonly ProviderRef R = new("fake", ProviderMode.Episode, "1");

    public Task<IReadOnlyList<SearchResult>> SearchSeriesAsync(SearchQuery q, IProviderConfig c, CancellationToken ct)
    {
        Searches++;
        IReadOnlyList<SearchResult> r = new[]
        {
            new SearchResult(new ProviderRef("fake", ProviderMode.Episode, "9"), Fx.Titles("Totally Different"), 1999, "series"),
            new SearchResult(R, Fx.Titles("Attack on Titan", "Shingeki no Kyojin"), 2013, "series"),
        };
        return Task.FromResult(q.Text.StartsWith("zzz") ? (IReadOnlyList<SearchResult>)Array.Empty<SearchResult>() : r);
    }

    public Task<Series> GetSeriesAsync(ProviderRef id, string l, IProviderConfig c, CancellationToken ct) =>
        Task.FromResult(new Series { Source = R, Titles = Fx.Titles("Attack on Titan") });

    public Task<IReadOnlyList<Episode>> GetEpisodesAsync(ProviderRef id, IReadOnlyCollection<string> l, IProviderConfig c, CancellationToken ct)
    {
        IReadOnlyList<Episode> eps = new[]
        {
            new Episode { Source = R, Titles = Fx.Titles("To You"), Number = new Numbering(1, 1, 1) },
            new Episode { Source = R, Titles = Fx.Titles("That Day"), Number = new Numbering(1, 2, 2) },
        };
        return Task.FromResult(eps);
    }
}

internal sealed class AmbiguousSeries : IEpisodeProvider
{
    public string ProviderId => "amb";
    public string DisplayName => "Amb";
    public ProviderMode Mode => ProviderMode.Episode;
    public ProviderCapabilities Capabilities => ProviderCapabilities.None;
    public IReadOnlyList<ConfigField> ConfigSchema => Array.Empty<ConfigField>();
    public Task<ConnectionTestResult> TestConnectionAsync(IProviderConfig c, CancellationToken ct) => Task.FromResult(new ConnectionTestResult(true, "ok"));

    public Task<IReadOnlyList<SearchResult>> SearchSeriesAsync(SearchQuery q, IProviderConfig c, CancellationToken ct)
    {
        IReadOnlyList<SearchResult> r = new[]
        {
            new SearchResult(new ProviderRef("amb", ProviderMode.Episode, "1"), Fx.Titles("Detective Conan"), null, "series"),
            new SearchResult(new ProviderRef("amb", ProviderMode.Episode, "2"), Fx.Titles("Detective Conan"), null, "series"),
        };
        return Task.FromResult(r);
    }

    public Task<Series> GetSeriesAsync(ProviderRef id, string l, IProviderConfig c, CancellationToken ct) =>
        Task.FromResult(new Series { Source = id, Titles = Fx.Titles("Detective Conan " + id.Id) });

    public Task<IReadOnlyList<Episode>> GetEpisodesAsync(ProviderRef id, IReadOnlyCollection<string> l, IProviderConfig c, CancellationToken ct)
    {
        IReadOnlyList<Episode> eps = new[] { new Episode { Source = id, Titles = Fx.Titles("Pilot"), Number = new Numbering(1, 1, 1) } };
        return Task.FromResult(eps);
    }
}

public class MatchServiceTests
{
    [Fact]
    public async Task Ambiguous_series_is_decided_by_the_chooser()
    {
        IReadOnlyList<SearchResult>? offered = null;
        var r = await new MatchService().MatchAsync(new[] { F("Detective Conan S01E01.mkv") }, ProviderMode.Episode,
            null, new AmbiguousSeries(), new NoConfig(), new NoConfig(), new MatchPreferences(), default,
            (title, files, candidates, ct) => { offered = candidates; return Task.FromResult<SearchResult?>(candidates[1]); });
        Assert.Equal(2, offered!.Count);
        Assert.Equal("Detective Conan 2", r[0].Series!.Titles.All[0].Value);
        Assert.Equal(MatchConfidence.Reliable, r[0].Confidence);
    }

    [Fact]
    public async Task Multi_episode_file_matches_every_episode()
    {
        var r = await new MatchService().MatchAsync(new[] { F("Attack.on.Titan.S01E01E02.mkv") }, ProviderMode.Episode,
            null, new FakeSeries(), new NoConfig(), new NoConfig(), new MatchPreferences(), default);
        Assert.Equal("To You", r[0].Episode!.Titles.All[0].Value);
        Assert.Single(r[0].MoreEpisodes);
        Assert.Equal("That Day", r[0].MoreEpisodes[0].Titles.All[0].Value);
        Assert.Null(MatchConsistency.Check(r[0]));
    }

    [Theory]
    [InlineData("Pilot|Second", "Pilot + Second")]
    [InlineData("Storia - Parte 1|Storia - Parte 2", "Storia Parte 1 + Parte 2")]
    [InlineData("Same|Same", "Same")]
    public void Multi_episode_titles_are_merged(string input, string expected) =>
        Assert.Equal(expected, MediaRenamer.Bindings.Catalog.MultiEpisodeTitles.Merge(input.Split('|')));

    [Fact]
    public async Task Skipping_the_chooser_leaves_the_file_unmatched()
    {
        var r = await new MatchService().MatchAsync(new[] { F("Detective Conan S01E01.mkv") }, ProviderMode.Episode,
            null, new AmbiguousSeries(), new NoConfig(), new NoConfig(), new MatchPreferences(), default,
            (title, files, candidates, ct) => Task.FromResult<SearchResult?>(null));
        Assert.Null(r[0].Series);
        Assert.Equal(MatchConfidence.None, r[0].Confidence);
    }

    private static MediaFile F(string path) => new()
    {
        Path = path,
        Detected = Parsing.FileNameParser.Parse(path).Info,
    };

    [Fact]
    public async Task Matches_episodes_and_searches_series_once()
    {
        var p = new FakeSeries();
        var r = await new MatchService().MatchAsync(
            new[] { F("Attack.on.Titan.S01E01.mkv"), F("Attack on Titan - S01E02.mkv") }, ProviderMode.Episode,
            null, p, new NoConfig(), new NoConfig(), new MatchPreferences());
        Assert.Equal(1, p.Searches);
        Assert.Equal("To You", r[0].Episode!.Titles.All[0].Value);
        Assert.Equal("That Day", r[1].Episode!.Titles.All[0].Value);
        Assert.Equal(MatchConfidence.Reliable, r[0].Confidence);
    }

    [Fact]
    public async Task Missing_episode_is_flagged_not_guessed()
    {
        var r = await new MatchService().MatchAsync(new[] { F("Attack on Titan S05E09.mkv") }, ProviderMode.Episode,
            null, new FakeSeries(), new NoConfig(), new NoConfig(), new MatchPreferences());
        Assert.NotNull(r[0].Series);
        Assert.Null(r[0].Episode);
        Assert.Equal(MatchConfidence.Uncertain, r[0].Confidence);
    }

    [Fact]
    public async Task Unknown_series_has_no_match()
    {
        var r = await new MatchService().MatchAsync(new[] { F("zzz nothing S01E01.mkv") }, ProviderMode.Episode,
            null, new FakeSeries(), new NoConfig(), new NoConfig(), new MatchPreferences());
        Assert.Equal(MatchConfidence.None, r[0].Confidence);
    }

    [Fact]
    public async Task Missing_provider_is_reported()
    {
        var r = await new MatchService().MatchAsync(new[] { F("Some.Movie.2010.mkv") }, ProviderMode.Movie,
            null, null, new NoConfig(), new NoConfig(), new MatchPreferences());
        Assert.Equal(MatchConfidence.None, r[0].Confidence);
        Assert.Contains("provider", r[0].Reasons[0]);
    }

    [Fact]
    public async Task Category_not_file_name_decides_series_or_movie()
    {
        var p = new FakeSeries();
        var r = await new MatchService().MatchAsync(new[] { F("Attack on Titan - 1x01.mkv") }, ProviderMode.Movie,
            null, p, new NoConfig(), new NoConfig(), new MatchPreferences());
        Assert.Equal(0, p.Searches);
        Assert.Null(r[0].Episode);
    }

    [Fact]
    public void Title_similarity_ignores_case_and_accents()
    {
        Assert.Equal(1, TitleSimilarity.Score("Amélie", "amelie"));
        Assert.True(TitleSimilarity.Score("Dune", "Dune Part Two") < 0.9);
    }
}

/// <summary>A provider whose search only understands the correctly spaced title.</summary>
internal sealed class SpacedOnlySeries : IEpisodeProvider
{
    private static readonly ProviderRef R = new("sp", ProviderMode.Episode, "1");
    public string ProviderId => "sp";
    public string DisplayName => "Spaced";
    public ProviderMode Mode => ProviderMode.Episode;
    public ProviderCapabilities Capabilities => ProviderCapabilities.None;
    public IReadOnlyList<ConfigField> ConfigSchema => Array.Empty<ConfigField>();
    public Task<ConnectionTestResult> TestConnectionAsync(IProviderConfig c, CancellationToken ct) => Task.FromResult(new ConnectionTestResult(true, "ok"));

    public Task<IReadOnlyList<SearchResult>> SearchSeriesAsync(SearchQuery q, IProviderConfig c, CancellationToken ct)
    {
        IReadOnlyList<SearchResult> r = q.Text.Contains(' ')
            ? new[] { new SearchResult(R, Fx.Titles("One Piece"), 1999, "series") }
            : Array.Empty<SearchResult>();
        return Task.FromResult(r);
    }

    public Task<Series> GetSeriesAsync(ProviderRef id, string l, IProviderConfig c, CancellationToken ct) =>
        Task.FromResult(new Series { Source = R, Titles = Fx.Titles("One Piece") });

    public Task<IReadOnlyList<Episode>> GetEpisodesAsync(ProviderRef id, IReadOnlyCollection<string> l, IProviderConfig c, CancellationToken ct)
    {
        IReadOnlyList<Episode> eps = new[] { new Episode { Source = R, Titles = Fx.Titles("Ep2"), Number = new Numbering(1, 2, 2) } };
        return Task.FromResult(eps);
    }
}
