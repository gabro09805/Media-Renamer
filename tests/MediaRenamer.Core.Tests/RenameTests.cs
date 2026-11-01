using MediaRenamer.Core.Renaming;
using MediaRenamer.Model;

namespace MediaRenamer.Core.Tests;

public class RenameTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mr-" + Guid.NewGuid().ToString("N"))).FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private string Touch(string name, string content = "x")
    {
        var p = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    private static MatchResult Ep(string path, int ep, string title = "Ep") => new()
    {
        File = new MediaFile { Path = path },
        Series = new Series { Source = new("f", ProviderMode.Episode, "1"), Titles = Fx.Titles("Show"), ReleaseDate = new DateOnly(2020, 1, 1) },
        Episode = new Episode { Source = new("f", ProviderMode.Episode, "1"), Titles = Fx.Titles(title), Number = new Numbering(1, ep) },
    };

    private static readonly RenameOptions Opt = new("{n}", "{n} - {s00e00} - {t}");

    [Fact]
    public void A_format_starting_with_a_drive_is_an_absolute_destination()
    {
        var v = Touch("a.mkv");
        var drive = Path.GetPathRoot(_dir)!.TrimEnd('\\');
        var plan = new RenamePlanner().Plan(new[] { Ep(v, 1) }, Array.Empty<SubtitleFile>(), new MatchPreferences(),
            new RenameOptions("{n}", "{drive}/MediaRenamerTest/{n}"));
        Assert.Equal(PlanState.Ready, plan[0].Status);
        Assert.Equal(Path.Combine(drive + "\\", "MediaRenamerTest", "Show.mkv"), plan[0].Target);

        var alone = new RenamePlanner().Plan(new[] { Ep(v, 1) }, Array.Empty<SubtitleFile>(), new MatchPreferences(), new RenameOptions("{n}", "{drive}"));
        Assert.Equal(PlanState.InvalidPath, alone[0].Status);
    }

    [Fact]
    public void Plans_video_and_subtitle_names()
    {
        var v = Touch("a.mkv");
        var s = Touch("a.ita.forced.srt");
        var sub = new SubtitleFile { Path = s, Role = FileRole.Subtitle, Language = "ita", Forced = true, LinkedVideoPath = v, Link = LinkConfidence.High };
        var plan = new RenamePlanner().Plan(new[] { Ep(v, 1, "Pilot") }, new[] { sub }, new MatchPreferences(), Opt);
        Assert.Equal(Path.Combine(_dir, "Show - S01E01 - Pilot.mkv"), plan[0].Target);
        Assert.Equal(Path.Combine(_dir, "Show - S01E01 - Pilot.ita.forced.srt"), plan[1].Target);
        Assert.All(plan, p => Assert.Equal(PlanState.Ready, p.Status));
    }

    [Fact]
    public void Standalone_subtitle_matched_on_its_own_is_renamed_keeping_language()
    {
        var s = Touch("a.ita.forced.srt");
        var sub = new SubtitleFile { Path = s, Role = FileRole.Subtitle, Language = "ita", Forced = true };
        var match = Ep(s, 1, "Pilot") with { File = sub };
        var plan = new RenamePlanner().Plan(new[] { match }, new[] { sub }, new MatchPreferences(), Opt);
        var item = Assert.Single(plan);
        Assert.True(item.IsSubtitle);
        Assert.Equal(PlanState.Ready, item.Status);
        Assert.Equal(Path.Combine(_dir, "Show - S01E01 - Pilot.ita.forced.srt"), item.Target);
    }

    [Fact]
    public void Low_confidence_subtitle_is_not_renamed()
    {
        var v = Touch("a.mkv");
        var sub = new SubtitleFile { Path = Touch("x.srt"), Role = FileRole.Subtitle, LinkedVideoPath = v, Link = LinkConfidence.Low };
        var plan = new RenamePlanner().Plan(new[] { Ep(v, 1) }, new[] { sub }, new MatchPreferences(), Opt);
        Assert.Equal(PlanState.NeedsReview, plan[1].Status);
    }

    [Fact]
    public void Detects_collision_and_existing_target()
    {
        var a = Touch("a.mkv");
        var b = Touch("b.mkv");
        Touch("Show - S01E02 - Ep.mkv");
        var plan = new RenamePlanner().Plan(new[] { Ep(a, 1), Ep(b, 1), Ep(Touch("c.mkv"), 2) }, Array.Empty<SubtitleFile>(), new MatchPreferences(), Opt);
        Assert.Equal(PlanState.Ready, plan[0].Status);
        Assert.Equal(PlanState.Collision, plan[1].Status);
        Assert.Equal(PlanState.TargetExists, plan[2].Status);
    }

    [Fact]
    public void Executes_without_overwrite_and_undoes()
    {
        var a = Touch("a.mkv", "A");
        var plan = new RenamePlanner().Plan(new[] { Ep(a, 1) }, Array.Empty<SubtitleFile>(), new MatchPreferences(), Opt);
        var journal = Path.Combine(_dir, "journal.json");
        var ex = new RenameExecutor();
        var r = ex.Execute(plan, journal);
        Assert.Equal(1, r.Renamed);
        Assert.False(File.Exists(a));
        Assert.True(File.Exists(plan[0].Target));

        var u = ex.Undo(journal);
        Assert.Equal(1, u.Renamed);
        Assert.Equal("A", File.ReadAllText(a));
    }

    [Fact]
    public void Swaps_names_through_temporary_files()
    {
        var a = Touch("Show - S01E01 - Ep.mkv", "first");
        var b = Touch("Show - S01E02 - Ep.mkv", "second");
        // a is episode 2 and b is episode 1: their targets are each other's source.
        var plan = new RenamePlanner().Plan(new[] { Ep(a, 2), Ep(b, 1) }, Array.Empty<SubtitleFile>(), new MatchPreferences(), Opt);
        Assert.All(plan, p => Assert.Equal(PlanState.Ready, p.Status));
        var r = new RenameExecutor().Execute(plan);
        Assert.Equal(2, r.Renamed);
        Assert.Equal("first", File.ReadAllText(b));
        Assert.Equal("second", File.ReadAllText(a));
    }

    [Fact]
    public void Moves_into_library_root_with_folders()
    {
        var a = Touch("a.mkv");
        var root = Path.Combine(_dir, "Library");
        var plan = new RenamePlanner().Plan(new[] { Ep(a, 1) }, Array.Empty<SubtitleFile>(), new MatchPreferences(),
            new RenameOptions("{n}", "{n}/Season {s}/{n} - {s00e00}", root));
        Assert.Equal(Path.Combine(root, "Show", "Season 1", "Show - S01E01.mkv"), plan[0].Target);
        new RenameExecutor().Execute(plan);
        Assert.True(File.Exists(plan[0].Target));
    }

    [Fact]
    public void Skips_a_file_that_disappeared_after_planning()
    {
        var a = Touch("a.mkv");
        var plan = new RenamePlanner().Plan(new[] { Ep(a, 1) }, Array.Empty<SubtitleFile>(), new MatchPreferences(), Opt);
        File.Delete(a);
        var r = new RenameExecutor().Execute(plan);
        Assert.Equal(0, r.Renamed);
        Assert.Single(r.Failures);
    }

    [Fact]
    public void Does_not_overwrite_a_file_created_after_planning()
    {
        var a = Touch("a.mkv", "A");
        var plan = new RenamePlanner().Plan(new[] { Ep(a, 1) }, Array.Empty<SubtitleFile>(), new MatchPreferences(), Opt);
        File.WriteAllText(plan[0].Target!, "other");
        var r = new RenameExecutor().Execute(plan);
        Assert.Equal(0, r.Renamed);
        Assert.Equal("other", File.ReadAllText(plan[0].Target!));
        Assert.Equal("A", File.ReadAllText(a));
    }
}
