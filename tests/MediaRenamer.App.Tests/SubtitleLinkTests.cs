using MediaRenamer.App.ViewModels;
using MediaRenamer.Model;

namespace MediaRenamer.App.Tests;

public class SubtitleLinkTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mr-sl-" + Guid.NewGuid().ToString("N"))).FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private string Touch(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public async Task A_subtitle_can_be_linked_by_hand_and_follows_its_video_when_renamed()
    {
        var services = new Env().CreateServices();
        services.Registry.Register(new FakePlugin(), new MediaRenamer.Infrastructure.PluginContext(
            new MediaRenamer.Infrastructure.Caching.MemoryResponseCache(), TimeSpan.FromHours(1), Path.GetTempPath(), () => new HttpClientHandler()));
        var vm = new RenameViewModel(services);
        var v1 = Touch("show s01e01.mkv");
        var v2 = Touch("show s01e02.mkv");
        var sub = Touch("completely unrelated name.ita.srt");
        vm.AddPaths(new[] { _dir });
        vm.SelectProvider(services.Registry.Find("fake", ProviderMode.Episode)!);

        vm.LinkSubtitle(sub, v2);
        await vm.MatchCommand.ExecuteAsync(null);

        var plan = vm.PendingRenames();
        var subItem = plan.Single(p => p.IsSubtitle);
        Assert.Equal(Path.GetFileNameWithoutExtension(plan.Single(p => p.Source == v2).Target!), Path.GetFileName(subItem.Target!)[..^8]);
        Assert.Equal(v2, vm.LinkedVideoOf(sub));
        // The subtitle row sits right under its video.
        Assert.Equal(sub, vm.Rows[vm.Rows.ToList().FindIndex(r => r.Path == v2) + 1].Path);

        vm.LinkSubtitle(sub, null);
        Assert.Null(vm.LinkedVideoOf(sub));
    }
}
