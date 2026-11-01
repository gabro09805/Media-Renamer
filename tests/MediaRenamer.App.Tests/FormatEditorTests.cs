using MediaRenamer.App.Services;
using MediaRenamer.App.ViewModels;
using MediaRenamer.Model;

namespace MediaRenamer.App.Tests;

public class FormatEditorTests
{
    private static (FormatEditorViewModel Vm, AppServices Services) Create(FormatKind kind = FormatKind.Series)
    {
        var services = new Env().CreateServices();
        return (new FormatEditorViewModel(services, kind), services);
    }

    [Fact]
    public void Opens_on_the_selected_format_with_a_preview()
    {
        var (vm, _) = Create();
        Assert.True(vm.IsBuiltIn);
        Assert.True(vm.IsValid);
        Assert.Equal("Breaking Bad - S01E02 - Cat's in the Bag.mkv", vm.Preview.Split('/').Last());
    }

    [Fact]
    public void Editing_a_custom_format_updates_the_preview_immediately()
    {
        var (vm, _) = Create();
        vm.DuplicateCommand.Execute(null);
        vm.Expression = "{n} - {sxe}";
        Assert.Equal("Breaking Bad - 1x02.mkv", vm.Preview);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Changing_the_output_language_updates_the_preview()
    {
        var (vm, services) = Create();
        vm.DuplicateCommand.Execute(null);
        vm.Expression = "{t}";
        Assert.Equal("Cat's in the Bag.mkv", vm.Preview);
        services.MatchPreferences.Save(services.MatchPreferences.Current with { OutputLanguage = "it" });
        Assert.Equal("Il gatto è nel sacco.mkv", vm.Preview);
    }

    [Fact]
    public void Invalid_expression_reports_the_position_and_cannot_be_saved()
    {
        var (vm, services) = Create();
        vm.DuplicateCommand.Execute(null);
        vm.Expression = "{n} {nope}";
        Assert.False(vm.IsValid);
        Assert.Contains(vm.Problems, p => p.Contains("Position"));
        var id = vm.SelectedFormat!.Id;
        vm.SaveCommand.Execute(null);
        Assert.NotEqual("{n} {nope}", services.Formats.Find(id)!.Expression);
    }

    [Fact]
    public void Save_persists_and_use_selects_the_format()
    {
        var (vm, services) = Create();
        vm.DuplicateCommand.Execute(null);
        vm.Name = "Mine";
        vm.Expression = "{n} {s00e00}";
        vm.UseCommand.Execute(null);
        Assert.Equal("{n} {s00e00}", services.Formats.Selected(FormatKind.Series).Expression);
        Assert.Equal("Mine", services.Formats.Selected(FormatKind.Series).Name);
    }

    [Fact]
    public void Built_in_formats_cannot_be_saved_or_deleted()
    {
        var (vm, services) = Create();
        var count = services.Formats.All.Count;
        vm.DeleteCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        Assert.Equal(count, services.Formats.All.Count);
        Assert.False(vm.CanEdit);
    }

    [Fact]
    public void Insert_adds_a_binding_at_the_caret()
    {
        var (vm, _) = Create();
        vm.DuplicateCommand.Execute(null);
        vm.Expression = "{n} - ";
        vm.Insert("{y}", vm.Expression.Length, out var caret);
        Assert.Equal("{n} - {y}", vm.Expression);
        Assert.Equal(9, caret);
    }

    [Theory]
    [InlineData(FormatKind.Movie, "Inception (2010)")]
    [InlineData(FormatKind.Series, "Breaking Bad")]
    public void Each_kind_has_its_own_sample(FormatKind kind, string expected)
    {
        var (vm, _) = Create(kind);
        Assert.Contains(expected, vm.Preview + "/" + vm.PreviewFolder);
    }

    [Fact]
    public void Spans_cover_the_expression()
    {
        var (vm, _) = Create();
        Assert.NotEmpty(vm.Spans);
    }

}
