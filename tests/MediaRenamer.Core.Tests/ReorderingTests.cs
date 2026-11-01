using MediaRenamer.Core.Matching;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Core.Tests;

public class ReorderingTests
{
    private static readonly ProviderRef R = new("fake", ProviderMode.Episode, "1");
    private static readonly Series S = new() { Source = R, Titles = Fx.Titles("Show") };

    private static Episode Ep(int n) =>
        new() { Source = new ProviderRef("fake", ProviderMode.Episode, "e" + n), Titles = Fx.Titles("E" + n), Number = new Numbering(1, n, n) };

    private static MediaFile F(string path) => new() { Path = path, Detected = Parsing.FileNameParser.Parse(path).Info };

    [Fact]
    public void MoveBlock_moves_selection_and_stops_at_the_edge()
    {
        var list = new List<string> { "a", "b", "c", "d" };
        var idx = MatchReordering.MoveBlock(list, new[] { 1, 2 }, -1);
        Assert.Equal(new[] { "b", "c", "a", "d" }, list);
        Assert.Equal(new[] { 0, 1 }, idx);

        idx = MatchReordering.MoveBlock(list, idx, -1);
        Assert.Equal(new[] { "b", "c", "a", "d" }, list);
        Assert.Equal(new[] { 0, 1 }, idx);
    }

    [Fact]
    public void Moving_a_new_name_shifts_the_others_and_leaves_files_in_place()
    {
        var files = new[] { F("Show.S01E01.mkv"), F("Show.S01E02.mkv"), F("Show.S01E03.mkv") };
        var slots = files.Select((f, i) => MatchService.Manual(f, S, Ep(i + 1), null, MatchReordering.OriginalOf(f))).ToList();

        MatchReordering.MoveBlock(slots, new[] { 0 }, 1);
        var result = MatchReordering.Reassign(files, slots);

        Assert.Equal(files.Select(f => f.Path), result.Select(r => r.File.Path));
        Assert.Equal(new int?[] { 2, 1, 3 }, result.Select(r => r.Episode!.Number.Episode).ToArray());
        Assert.NotNull(MatchConsistency.Check(result[0]));
        Assert.NotNull(MatchConsistency.Check(result[1]));
        Assert.Null(MatchConsistency.Check(result[2]));
    }

    [Fact]
    public void Consistent_match_has_no_warning()
    {
        var f = F("Show.S01E02.mkv");
        Assert.Null(MatchConsistency.Check(MatchService.Manual(f, S, Ep(2), null, MatchReordering.OriginalOf(f))));
    }

    [Fact]
    public void Unrelated_title_is_flagged()
    {
        var f = F("Completely Other Name S01E01.mkv");
        Assert.NotNull(MatchConsistency.Check(MatchService.Manual(f, S, Ep(1), null, MatchReordering.OriginalOf(f))));
    }
}
