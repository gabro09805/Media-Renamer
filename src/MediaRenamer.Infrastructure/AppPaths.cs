namespace MediaRenamer.Infrastructure;

/// <summary>Application folders. A "portable.txt" next to the executable switches to a local "data" folder.</summary>
public sealed class AppPaths
{
    public AppPaths(string root)
    {
        Root = root;
        Config = Path.Combine(root, "config");
        Cache = Path.Combine(root, "cache");
        Data = Path.Combine(root, "data");
        Plugins = Path.Combine(root, "plugins");
    }

    public string Root { get; }
    public string Config { get; }
    public string Cache { get; }
    public string Data { get; }
    public string Plugins { get; }

    public static AppPaths Resolve()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "portable.txt");
        var root = File.Exists(portable)
            ? Path.Combine(AppContext.BaseDirectory, "userdata")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaRenamer");
        return new AppPaths(root);
    }

    public void EnsureCreated()
    {
        foreach (var d in new[] { Config, Cache, Data, Plugins }) Directory.CreateDirectory(d);
    }
}
