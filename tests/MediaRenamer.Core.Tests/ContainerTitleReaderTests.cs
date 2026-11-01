using System.Text;
using MediaRenamer.Core.Import;

namespace MediaRenamer.Core.Tests;

public class ContainerTitleReaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mr-ct-" + Guid.NewGuid().ToString("N"))).FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private static byte[] Box(string type, params byte[][] body)
    {
        var payload = body.SelectMany(b => b).ToArray();
        var size = payload.Length + 8;
        return new byte[] { (byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size }
            .Concat(Encoding.Latin1.GetBytes(type)).Concat(payload).ToArray();
    }

    private string Write(string name, byte[] bytes)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    [Fact]
    public void Reads_the_title_of_a_matroska_file()
    {
        var title = Encoding.UTF8.GetBytes("Inception (2010)");
        var bytes = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x80 }
            .Concat(new byte[] { 0x18, 0x53, 0x80, 0x67, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })
            .Concat(new byte[] { 0x15, 0x49, 0xA9, 0x66, (byte)(0x80 | (3 + title.Length)), 0x7B, 0xA9, (byte)(0x80 | title.Length) })
            .Concat(title).ToArray();
        Assert.Equal("Inception (2010)", ContainerTitleReader.ReadTitle(Write("x.mkv", bytes)));
    }

    [Fact]
    public void Reads_the_title_of_an_mp4_file_even_when_moov_is_after_the_media()
    {
        var text = Encoding.UTF8.GetBytes("Dune Part Two");
        var data = Box("data", new byte[] { 0, 0, 0, 1, 0, 0, 0, 0 }, text);
        var nam = Box("\u00A9nam", data);
        var ilst = Box("ilst", nam);
        var meta = Box("meta", new byte[] { 0, 0, 0, 0 }, ilst);
        var moov = Box("moov", Box("udta", meta));
        var bytes = Box("ftyp", Encoding.Latin1.GetBytes("isom\0\0\0\0")).Concat(Box("mdat", new byte[100])).Concat(moov).ToArray();
        Assert.Equal("Dune Part Two", ContainerTitleReader.ReadTitle(Write("x.mp4", bytes)));
    }

    [Fact]
    public void Garbage_or_missing_files_return_null()
    {
        Assert.Null(ContainerTitleReader.ReadTitle(Write("bad.mkv", new byte[] { 1, 2, 3 })));
        Assert.Null(ContainerTitleReader.ReadTitle(Path.Combine(_dir, "none.mp4")));
    }

    [Fact]
    public void Importer_uses_the_embedded_title_only_when_the_file_name_is_weak()
    {
        var title = Encoding.UTF8.GetBytes("Inception (2010)");
        var mkv = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x80, 0x18, 0x53, 0x80, 0x67, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0x15, 0x49, 0xA9, 0x66, (byte)(0x80 | (3 + title.Length)), 0x7B, 0xA9, (byte)(0x80 | title.Length) }
            .Concat(title).ToArray();
        var weak = Write("1234.mkv", mkv);
        var strong = Write("Matrix 1999.mkv", mkv);
        var r = FileImporter.Import(new[] { weak, strong });
        Assert.Equal("Inception", r.Videos.Single(v => v.Path == weak).Detected.Title);
        Assert.Equal(2010, r.Videos.Single(v => v.Path == weak).Detected.Year);
        Assert.Equal("Matrix", r.Videos.Single(v => v.Path == strong).Detected.Title);
    }
}
