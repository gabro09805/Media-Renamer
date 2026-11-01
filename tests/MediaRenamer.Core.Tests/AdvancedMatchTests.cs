using MediaRenamer.Core.Matching;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Core.Tests;

public class AdvancedMatchTests
{
    private static Series Show(string id) => new() { Source = new ProviderRef("fake", ProviderMode.Episode, id), Titles = Fx.Titles("Show " + id) };

    private static Episode Ep(int s, int n, params (string Title, string Lang)[] titles) => new()
    {
        Source = new ProviderRef("fake", ProviderMode.Episode, $"{s}-{n}"),
        Titles = new TitleSet(titles.Select((t, i) => new LocalizedText(t.Title, t.Lang, i == 0 ? TitleKind.Primary : TitleKind.Localized))),
        Number = new Numbering(s, n, n),
    };

    private static MediaFile F(string path) => new() { Path = path, Detected = Parsing.FileNameParser.Parse(path).Info };

    [Fact]
    public void Episode_is_identified_by_an_english_title_when_the_provider_title_is_italian()
    {
        var episodes = new[]
        {
            Ep(1, 1, ("Il primo giorno", "it"), ("The First Day", "en")),
            Ep(1, 2, ("Il ritorno", "it"), ("The Return", "en")),
        };
        var d = new DetectedInfo { EpisodeTitle = "The Return" };

        var found = MatchService.FindEpisodeByTitle(episodes, d, out _);

        Assert.Equal(2, found!.Number.Episode);
    }

    [Fact]
    public void Ambiguous_or_missing_title_finds_nothing()
    {
        var episodes = new[] { Ep(1, 1, ("Pilot", "en")), Ep(2, 1, ("Pilot", "en")) };
        Assert.Null(MatchService.FindEpisodeByTitle(episodes, new DetectedInfo { EpisodeTitle = "Pilot" }, out _));
        Assert.Null(MatchService.FindEpisodeByTitle(episodes, new DetectedInfo(), out _));
    }

    [Fact]
    public void Apply_to_all_keeps_episodes_distinct_and_ignores_other_series()
    {
        var oldShow = Show("1");
        var newShow = Show("2");
        var other = Show("9");
        var files = new[] { F("Show.S01E01.mkv"), F("Show.S01E02.mkv"), F("Other.S01E01.mkv") };
        var old = files.Select((f, i) => MatchService.Manual(f, i == 2 ? other : oldShow, Ep(1, i + 1), null, MatchReordering.OriginalOf(f))).ToList();

        var newEpisodes = new[] { Ep(1, 1, ("A", "en")), Ep(1, 2, ("B", "en")) };
        var edited = MatchService.Manual(files[0], newShow, newEpisodes[0], null, MatchReordering.OriginalOf(files[0]));

        var result = MatchService.ReapplySeries(old, old[0], edited, newShow, newEpisodes, new MatchPreferences());

        Assert.Equal(new[] { 1, 2 }, result.Take(2).Select(r => r.Episode!.Number.Episode!.Value).ToArray());
        Assert.All(result.Take(2), r => Assert.Equal(newShow.Source, r.Series!.Source));
        Assert.Equal(other.Source, result[2].Series!.Source);
    }

    [Fact]
    public void Apply_to_all_flags_files_whose_episode_does_not_exist_in_the_new_series()
    {
        var oldShow = Show("1");
        var newShow = Show("2");
        var files = new[] { F("Show.S01E01.mkv"), F("Show.S01E07.mkv") };
        var old = files.Select((f, i) => MatchService.Manual(f, oldShow, Ep(1, i + 1), null, MatchReordering.OriginalOf(f))).ToList();
        var newEpisodes = new[] { Ep(1, 1, ("A", "en")) };
        var edited = MatchService.Manual(files[0], newShow, newEpisodes[0], null, null);

        var result = MatchService.ReapplySeries(old, old[0], edited, newShow, newEpisodes, new MatchPreferences());

        Assert.Null(result[1].Episode);
        Assert.Equal(newShow.Source, result[1].Series!.Source);
        Assert.Equal(MatchConfidence.Uncertain, result[1].Confidence);
    }
}
