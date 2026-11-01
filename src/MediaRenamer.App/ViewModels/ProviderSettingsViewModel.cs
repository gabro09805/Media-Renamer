using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaRenamer.App.Services;
using MediaRenamer.Plugin.Abstractions;
using MediaRenamer.PluginHost;

namespace MediaRenamer.App.ViewModels;

public sealed partial class ProviderFieldViewModel : ObservableObject
{
    public ProviderFieldViewModel(ConfigField field, string? value)
    {
        Field = field;
        _value = value ?? "";
    }

    public ConfigField Field { get; }
    public string Key => Field.Key;
    public string Label => Field.Required ? Field.Label + " *" : Field.Label;
    public bool IsSecret => Field.Type == ConfigFieldType.Secret;
    public char PasswordChar => IsSecret ? '●' : '\0';

    [ObservableProperty]
    private string _value;
}

/// <summary>
/// One card per provider: credentials are entered once and shared by all its modes,
/// and a single switch enables or disables the whole provider.
/// </summary>
public sealed partial class ProviderSettingsViewModel : ObservableObject
{
    private readonly AppServices _services;

    public ProviderSettingsViewModel(IReadOnlyList<ProviderEntry> entries, AppServices services)
    {
        _services = services;
        Entries = entries;
        _isEnabled = entries.Any(e => e.IsEnabled);

        var first = entries[0].Provider;
        var stored = services.Settings.GetValues(first);
        Fields = new ObservableCollection<ProviderFieldViewModel>(
            first.ConfigSchema.Select(f => new ProviderFieldViewModel(f, stored.GetValueOrDefault(f.Key))));
    }

    public IReadOnlyList<ProviderEntry> Entries { get; }
    public string Title => Entries[0].Provider.DisplayName;
    public ObservableCollection<ProviderFieldViewModel> Fields { get; }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _statusIsError;

    [ObservableProperty]
    private bool _isBusy;

    private Dictionary<string, string> CurrentValues() => Fields.ToDictionary(f => f.Key, f => f.Value.Trim());

    [RelayCommand]
    private void Save()
    {
        var values = CurrentValues();
        if (IsEnabled)
        {
            var missing = Fields.Where(f => f.Field.Required && values[f.Key].Length == 0).Select(f => f.Field.Label).ToList();
            if (missing.Count > 0)
            {
                SetStatus("Required: " + string.Join(", ", missing), true);
                return;
            }
        }

        foreach (var entry in Entries)
        {
            _services.Settings.Save(entry.Provider, IsEnabled, values);
            _services.Registry.SetEnabled(entry, IsEnabled);
        }
        SetStatus("Saved.", false);
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        IsBusy = true;
        try
        {
            var config = new DictionaryProviderConfig(CurrentValues());
            var result = await Entries[0].Provider.TestConnectionAsync(config, CancellationToken.None);
            SetStatus((result.Success ? "✔ " : "✘ ") + result.Message, !result.Success);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetStatus("✘ " + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetStatus(string text, bool error)
    {
        Status = text;
        StatusIsError = error;
    }
}
