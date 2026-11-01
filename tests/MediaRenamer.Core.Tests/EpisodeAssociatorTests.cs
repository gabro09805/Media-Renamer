using MediaRenamer.Core.Matching;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Core.Tests;

public class EpisodeAssociatorTests
{
    private static readonly ProviderRef R = new("fake", ProviderMode.Episode, "1");
    private static readonly Series S = new() { Source = R, Titles = Fx.Titles("Show") };

    private static Episode Ep(int season, int number) =>
        new() { Source = new ProviderRef("fake", ProviderMode.Episode, $"{season}-{number}"), Titles = Fx.Titles("E" + number), Number = new Numbering(season, number, number) };

    private static MediaFile F(string path) => new() { Path = path, Detected = Parsing.FileNameParser.Parse(path).Info };

    [Fact]
    public void Search_order_ignores_file_numbering()
    {
        var files = new[] { F("video_01.mkv"), F("video_02.mkv"), F("video_03.mkv") };
        var selected = new[] { Ep(1, 5), Ep(1, 8), Ep(1, 12) };

        var plan = EpisodeAssociator.BySearchOrder(files, S, selected);

        Assert.Null(plan.Problem);
        Assert.Equal(new int?[] { 5, 8, 12 }, plan.Matches.Select(m => m.Episode!.Number.Episode).ToArray());
        Assert.All(plan.Matches, m => Assert.Equal(AssociationMode.SearchOrder, m.Association));
    }

    [Fact]
    public void Search_order_reports_count_mismatch()
    {
        var plan = EpisodeAssociator.BySearchOrder(new[] { F("a.mkv"), F("b.mkv") }, S, new[] { Ep(1, 1) });

        Assert.NotNull(plan.Problem);
        Assert.Null(plan.Matches[1].Episode);
    }

    [Fact]
    public void Episode_number_matches_only_selected_episodes()
    {
        var files = new[] { F("Show.S01E05.mkv"), F("Show.S01E08.mkv"), F("Show.S01E06.mkv") };
        var selected = new[] { Ep(1, 5), Ep(1, 8) };

        var plan = EpisodeAssociator.ByEpisodeNumber(files, S, selected, new MatchPreferences());

        Assert.Equal(5, plan.Matches[0].Episode!.Number.Episode);
        Assert.Equal(8, plan.Matches[1].Episode!.Number.Episode);
        Assert.Null(plan.Matches[2].Episode);
        Assert.Null(plan.Problem);
    }

    [Fact]
    public void Episode_number_with_no_hits_is_a_problem()
    {
        var plan = EpisodeAssociator.ByEpisodeNumber(new[] { F("Show.S02E01.mkv") }, S, new[] { Ep(1, 1) }, new MatchPreferences());
        Assert.NotNull(plan.Problem);
    }
}
