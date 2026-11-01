using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.PluginHost;

/// <summary>plugin.json: { "id": "...", "entry": "MyPlugin.dll" }</summary>
public sealed record PluginManifest(string Id, string Entry);

public sealed record PluginLoadResult(string Folder, bool Success, string? Error, IReadOnlyList<string> ProviderKeys);

/// <summary>
/// Loads external plugins from "plugins/&lt;name&gt;/plugin.json". Each plugin gets its own load context
/// (own dependencies); the shared contract assemblies always come from the application.
/// Plugins are native .NET code with the same permissions as the app: only install trusted ones.
/// </summary>
public sealed class PluginLoader
{
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "MediaRenamer.Model", "MediaRenamer.Plugin.Abstractions",
    };

    public IReadOnlyList<PluginLoadResult> LoadAll(string pluginsDirectory, ProviderRegistry registry, IPluginContext context)
    {
        if (!Directory.Exists(pluginsDirectory)) return Array.Empty<PluginLoadResult>();
        return Directory.EnumerateDirectories(pluginsDirectory)
            .Where(d => File.Exists(Path.Combine(d, "plugin.json")))
            .Select(d => Load(d, registry, context))
            .ToList();
    }

    public PluginLoadResult Load(string folder, ProviderRegistry registry, IPluginContext context)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(
                File.ReadAllText(Path.Combine(folder, "plugin.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Empty plugin.json.");

            var assemblyPath = Path.GetFullPath(Path.Combine(folder, manifest.Entry));
            if (!assemblyPath.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Plugin entry must be inside the plugin folder.");

            var assembly = new PluginLoadContext(assemblyPath).LoadFromAssemblyPath(assemblyPath);
            var types = assembly.GetTypes()
                .Where(t => typeof(IProviderPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
                .ToList();
            if (types.Count == 0) throw new InvalidDataException($"No IProviderPlugin found in {manifest.Entry}.");

            var keys = new List<string>();
            foreach (var type in types)
            {
                var plugin = (IProviderPlugin)Activator.CreateInstance(type)!;
                keys.AddRange(registry.Register(plugin, context).Select(e => e.Key));
            }
            return new PluginLoadResult(folder, true, null, keys);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new PluginLoadResult(folder, false, ex.Message, Array.Empty<string>());
        }
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string path) : base(isCollectible: false) => _resolver = new AssemblyDependencyResolver(path);

        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name is not null && SharedAssemblies.Contains(name.Name)) return null;
            var path = _resolver.ResolveAssemblyToPath(name);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
