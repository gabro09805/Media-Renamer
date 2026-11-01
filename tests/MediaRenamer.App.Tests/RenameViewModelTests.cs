using MediaRenamer.App.ViewModels;
using MediaRenamer.Core.Renaming;
using MediaRenamer.Model;

namespace MediaRenamer.App.Tests;

public class RenameViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mr-vm-" + Guid.NewGuid().ToString("N"))).FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private string Touch(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        return p;
    }

    private static RenameViewModel Create(out AppServicesHolder holder)
    {
        var services = new Env().CreateServices();
        var plugin = new FakePlugin();
        services.Registry.Register(plugin, new MediaRenamer.Infrastructure.PluginContext(
            new MediaRenamer.Infrastructure.Caching.MemoryResponseCache(), TimeSpan.FromHours(1), Path.GetTempPath(), () => new HttpClientHandler()));
        var vm = new RenameViewModel(services);
        holder = new AppServicesHolder(services);
        return vm;
    }

    internal sealed record AppServicesHolder(MediaRenamer.App.Services.AppServices Services);

    [Fact]
    public void Loads_videos_with_their_subtitles_nested()
    {
        var vm = Create(out _);
        Touch("Show.S01E01.mkv");
        Touch("Show.S01E01.ita.srt");
        Touch("notes.txt");
        vm.AddPaths(new[] { _dir });
        Assert.Equal(2, vm.Rows.Count);
        Assert.False(vm.Rows[0].IsSubtitle);
        Assert.True(vm.Rows[1].IsSubtitle);
    }

    [Fact]
    public async Task Removing_a_file_keeps_the_matches_of_the_others_aligned()
    {
        var vm = Create(out var h);
        Touch("show s01e01.mkv");
        Touch("show s01e02.mkv");
        Touch("show s01e03.mkv");
        vm.AddPaths(new[] { _dir });
        vm.SelectProvider(h.Services.Registry.Find("fake", ProviderMode.Episode)!);
        await vm.MatchCommand.ExecuteAsync(null);

        vm.RemoveRows(new[] { vm.Rows[0] });

        Assert.Equal(2, vm.Rows.Count);
        Assert.All(vm.Rows, r => Assert.NotNull(r.State));
        Assert.Contains("S01E02", vm.Rows[0].DisplayNewName);
    }

    [Fact]
    public async Task Match_then_rename_and_undo()
    {
        var vm = Create(out var h);
        var video = Touch("show s01e01.mkv");
        var sub = Touch("show s01e01.ita.srt");
        vm.AddPaths(new[] { _dir });
        vm.SelectProvider(h.Services.Registry.Find("fake", ProviderMode.Episode)!);

        await vm.MatchCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.ReadyCount);

        var plan = vm.PendingRenames();
        var outcome = vm.ApplyRename(plan);
        Assert.Equal(2, outcome.Renamed);
        Assert.False(File.Exists(video));
        Assert.False(File.Exists(sub));
        Assert.True(File.Exists(plan.Single(p => !p.IsSubtitle).Target));
        Assert.EndsWith(".ita.srt", plan.Single(p => p.IsSubtitle).Target);
        Assert.True(vm.CanUndo);

        vm.UndoCommand.Execute(null);
        Assert.True(File.Exists(video));
        Assert.True(File.Exists(sub));
    }

    [Fact]
    public async Task Without_a_selected_provider_nothing_is_renamed()
    {
        var vm = Create(out _);
        Touch("show s01e01.mkv");
        vm.AddPaths(new[] { _dir });
        await vm.MatchCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.ReadyCount);
        Assert.Empty(vm.PendingRenames());
    }
}
