using System.Text.Json;

namespace MediaRenamer.App.Services;

/// <summary>General application settings persisted as JSON (currently only the library root folder).</summary>
public sealed class AppSettingsStore
{
    private sealed record Data(string LibraryRoot = "");

    private readonly string _path;
    private Data _data;

    public AppSettingsStore(string path)
    {
        _path = path;
        _data = Load();
    }

    /// <summary>Empty means files are renamed in place.</summary>
    public string LibraryRoot
    {
        get => _data.LibraryRoot;
        set
        {
            _data = _data with { LibraryRoot = value?.Trim() ?? "" };
            Save();
        }
    }

    private Data Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(_path)) ?? new Data() : new Data();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Data();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_data));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not persisting is acceptable: the value stays valid for this session.
        }
    }
}
