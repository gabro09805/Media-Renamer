using MediaRenamer.Infrastructure.Settings;
using MediaRenamer.Model;

namespace MediaRenamer.Infrastructure.Tests;

public class FormatStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mr-fmt-" + Guid.NewGuid().ToString("N"));

    public FormatStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private FormatStore New() => new(Path.Combine(_dir, "formats.json"));

    [Fact]
    public void Defaults_to_built_in_format_per_kind()
    {
        var store = New();
        foreach (var kind in Enum.GetValues<FormatKind>())
            Assert.Equal(BuiltInFormats.Default(kind).Id, store.Selected(kind).Id);
    }

    [Fact]
    public void Custom_format_persists_and_selection_survives_reload()
    {
        var store = New();
        var saved = store.Save(new RenameFormat("", "Mine", FormatKind.Series, "{n} {s00e00}"));
        store.Select(FormatKind.Series, saved.Id);

        var reloaded = New();
        Assert.Equal("{n} {s00e00}", reloaded.Selected(FormatKind.Series).Expression);
        Assert.False(reloaded.Selected(FormatKind.Series).IsBuiltIn);
    }

    [Fact]
    public void Built_in_formats_are_immutable()
    {
        var store = New();
        var builtIn = BuiltInFormats.All[0];
        Assert.Throws<InvalidOperationException>(() => store.Save(builtIn with { Expression = "{n}" }));
        Assert.Throws<InvalidOperationException>(() => store.Delete(builtIn.Id));
    }

    [Fact]
    public void Duplicate_creates_editable_copy_with_unique_name()
    {
        var store = New();
        var a = store.Duplicate(BuiltInFormats.All[0].Id);
        var b = store.Duplicate(BuiltInFormats.All[0].Id);
        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(a.Name, b.Name);
        Assert.False(a.IsBuiltIn);
    }

    [Fact]
    public void Deleting_the_selected_format_falls_back_to_default()
    {
        var store = New();
        var f = store.Save(new RenameFormat("", "X", FormatKind.Movie, "{n}"));
        store.Select(FormatKind.Movie, f.Id);
        store.Delete(f.Id);
        Assert.Equal(BuiltInFormats.Default(FormatKind.Movie).Id, store.Selected(FormatKind.Movie).Id);
    }

    [Fact]
    public void Export_then_import_adds_new_copies_and_skips_exact_duplicates()
    {
        var a = New();
        var f = a.Save(new RenameFormat("", "Shared", FormatKind.Movie, "{n} {absolute}"));
        var file = Path.Combine(_dir, "export.json");
        a.Export(new[] { f.Id }, file);

        var b = new FormatStore(Path.Combine(_dir, "other.json"));
        Assert.Equal(1, b.Import(file));
        Assert.Equal(0, b.Import(file));
        Assert.Contains(b.Of(FormatKind.Movie), x => x.Name == "Shared" && x.Expression == "{n} {absolute}");
    }

    [Fact]
    public void Import_rejects_invalid_files()
    {
        var file = Path.Combine(_dir, "bad.json");
        File.WriteAllText(file, "not json");
        Assert.Throws<FormatException>(() => New().Import(file));
    }

    [Fact]
    public void Corrupt_store_file_falls_back_to_defaults()
    {
        File.WriteAllText(Path.Combine(_dir, "formats.json"), "{ broken");
        Assert.Equal(BuiltInFormats.Default(FormatKind.Movie).Id, New().Selected(FormatKind.Movie).Id);
    }

    [Fact]
    public void Changed_event_fires_on_save()
    {
        var store = New();
        var count = 0;
        store.Changed += () => count++;
        store.Save(new RenameFormat("", "Y", FormatKind.Movie, "{n}"));
        Assert.Equal(1, count);
    }
}
