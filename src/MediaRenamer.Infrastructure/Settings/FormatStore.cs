using System.Text.Json;
using System.Text.Json.Serialization;
using MediaRenamer.Model;

namespace MediaRenamer.Infrastructure.Settings;

/// <summary>Built-in formats plus the user's custom ones, the selected format per kind, and import/export.</summary>
public sealed class FormatStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class StoreFile
    {
        public Dictionary<FormatKind, string> Selected { get; set; } = new();
        public List<RenameFormat> Custom { get; set; } = new();
    }

    private sealed class ExportFile
    {
        public int Version { get; set; } = 1;
        public List<RenameFormat> Formats { get; set; } = new();
    }

    private readonly string _path;
    private StoreFile _data;

    public FormatStore(string path)
    {
        _path = path;
        _data = Load();
    }

    public event Action? Changed;

    public IReadOnlyList<RenameFormat> All => BuiltInFormats.All.Concat(_data.Custom).ToList();

    public IReadOnlyList<RenameFormat> Of(FormatKind kind) => All.Where(f => f.Kind == kind).ToList();

    public RenameFormat? Find(string id) => All.FirstOrDefault(f => f.Id == id);

    public RenameFormat Selected(FormatKind kind) =>
        (_data.Selected.TryGetValue(kind, out var id) ? Of(kind).FirstOrDefault(f => f.Id == id) : null)
        ?? BuiltInFormats.Default(kind);

    public void Select(FormatKind kind, string id)
    {
        var format = Of(kind).FirstOrDefault(f => f.Id == id) ?? throw new ArgumentException("Unknown format for this kind.", nameof(id));
        _data.Selected[kind] = format.Id;
        Persist();
    }

    /// <summary>Creates or updates a custom format (an empty id creates a new one). Built-in formats cannot be changed: duplicate them instead.</summary>
    public RenameFormat Save(RenameFormat format)
    {
        if (string.IsNullOrWhiteSpace(format.Name)) throw new ArgumentException("A format needs a name.", nameof(format));
        if (string.IsNullOrWhiteSpace(format.Expression)) throw new ArgumentException("A format needs an expression.", nameof(format));
        if (BuiltInFormats.All.Any(b => b.Id == format.Id)) throw new InvalidOperationException("Built-in formats cannot be modified.");

        var saved = format with { Name = format.Name.Trim(), IsBuiltIn = false, Id = string.IsNullOrWhiteSpace(format.Id) ? NewId() : format.Id };
        var index = _data.Custom.FindIndex(f => f.Id == saved.Id);
        if (index >= 0) _data.Custom[index] = saved;
        else _data.Custom.Add(saved);
        Persist();
        return saved;
    }

    public RenameFormat Duplicate(string id, string? name = null)
    {
        var source = Find(id) ?? throw new ArgumentException("Unknown format.", nameof(id));
        return Save(source with { Id = "", IsBuiltIn = false, Name = name ?? UniqueName(source.Name + " (copy)") });
    }

    public void Delete(string id)
    {
        if (BuiltInFormats.All.Any(b => b.Id == id)) throw new InvalidOperationException("Built-in formats cannot be deleted.");
        if (_data.Custom.RemoveAll(f => f.Id == id) == 0) return;
        foreach (var kind in _data.Selected.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList()) _data.Selected.Remove(kind);
        Persist();
    }

    public void Export(IEnumerable<string> ids, string path)
    {
        var wanted = ids.ToHashSet();
        var file = new ExportFile { Formats = All.Where(f => wanted.Contains(f.Id)).Select(f => f with { Id = "" }).ToList() };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(file, Options));
    }

    /// <summary>Imports formats as new custom formats (never overwriting anything). Returns how many were added; exact duplicates are skipped.</summary>
    /// <exception cref="FormatException">The file is not a valid format export.</exception>
    public int Import(string path)
    {
        ExportFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ExportFile>(File.ReadAllText(path), Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException("This file is not a valid MediaRenamer format file: " + ex.Message, ex);
        }

        if (file is null || file.Formats.Count == 0) throw new FormatException("The file does not contain any format.");

        var added = 0;
        foreach (var f in file.Formats)
        {
            if (string.IsNullOrWhiteSpace(f.Name) || string.IsNullOrWhiteSpace(f.Expression) || !Enum.IsDefined(f.Kind))
                throw new FormatException("The file contains a format without name, kind or expression.");
            if (All.Any(x => x.Kind == f.Kind && x.Expression == f.Expression && x.Name == f.Name)) continue;

            _data.Custom.Add(f with { Id = NewId(), IsBuiltIn = false, Kind = Normalize(f.Kind), Name = UniqueName(f.Name.Trim()) });
            added++;
        }

        if (added > 0) Persist();
        return added;
    }

    private string UniqueName(string name)
    {
        var existing = All.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(name)) return name;
        for (var i = 2; ; i++)
        {
            var candidate = $"{name} {i}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    private static string NewId() => "custom." + Guid.NewGuid().ToString("N")[..8];

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_data, Options));
        File.Move(tmp, _path, overwrite: true);
        Changed?.Invoke();
    }

    private static FormatKind Normalize(FormatKind kind) => kind == FormatKind.Anime ? FormatKind.Series : kind;

    private StoreFile Load()
    {
        if (!File.Exists(_path)) return new StoreFile();
        try
        {
            var loaded = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(_path), Options) ?? new StoreFile();
            loaded.Custom = loaded.Custom
                .Where(f => !string.IsNullOrWhiteSpace(f.Id) && !string.IsNullOrWhiteSpace(f.Expression) && !BuiltInFormats.All.Any(b => b.Id == f.Id))
                .Select(f => f with { IsBuiltIn = false, Kind = Normalize(f.Kind) })
                .ToList();
            loaded.Selected = loaded.Selected
                .GroupBy(kv => Normalize(kv.Key))
                .ToDictionary(g => g.Key, g => g.Last().Value);
            return loaded;
        }
        catch (JsonException)
        {
            return new StoreFile();
        }
    }
}
