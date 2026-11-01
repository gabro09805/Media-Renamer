using MediaRenamer.Core.Import;
using MediaRenamer.Core.Subtitles;
using MediaRenamer.Model;

namespace MediaRenamer.Core.Tests;

public class SubtitleTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mr-" + Guid.NewGuid().ToString("N"))).FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private string Touch(string rel)
    {
        var p = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void Detects_language_and_flags()
    {
        var (b, s) = SubtitleDetector.Detect("Show.S01E01.ita.forced.srt");
        Assert.Equal("Show.S01E01", b);
        Assert.Equal("ita", s.Language);
        Assert.True(s.Forced);
        Assert.Equal(SubtitleFormat.Srt, s.Format);

        var (_, s2) = SubtitleDetector.Detect("Movie.2010.English.SDH.ass");
        Assert.Equal("eng", s2.Language);
        Assert.True(s2.Sdh);
    }

    [Fact]
    public void Links_by_base_name_and_by_episode()
    {
        var v1 = Touch("Show.S01E01.mkv");
        var v2 = Touch("Show.S01E02.mkv");
        Touch("Show.S01E01.ita.srt");
        Touch("Subs/Show.S01E02.eng.srt");
        var r = FileImporter.Import(new[] { _dir });
        Assert.Equal(2, r.Videos.Count);
        Assert.Equal(v1, r.Subtitles.Single(s => s.Language == "ita").LinkedVideoPath);
        var e = r.Subtitles.Single(s => s.Language == "eng");
        Assert.Equal(v2, e.LinkedVideoPath);
        Assert.Equal(LinkConfidence.High, e.Link);
    }

    [Fact]
    public void Ambiguous_subtitle_is_not_guessed()
    {
        Touch("A.2001.mkv");
        Touch("B.2002.mkv");
        Touch("Other.ita.srt");
        var r = FileImporter.Import(new[] { _dir });
        Assert.Equal(LinkConfidence.None, r.Subtitles.Single().Link);
        Assert.Null(r.Subtitles.Single().LinkedVideoPath);
    }

    [Fact]
    public void Lone_video_gives_low_confidence()
    {
        Touch("Movie.2010.mkv");
        Touch("whatever.eng.srt");
        var r = FileImporter.Import(new[] { _dir });
        Assert.Equal(LinkConfidence.Low, r.Subtitles.Single().Link);
    }
}
