using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaRenamer.App.Services;
using MediaRenamer.Model;
using MediaRenamer.PluginHost;

namespace MediaRenamer.App.ViewModels;

public sealed partial class PreferencesViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public PreferencesViewModel(AppServices services)
    {
        _services = services;
        Providers = new ObservableCollection<ProviderSettingsViewModel>(
            services.Registry.All.GroupBy(e => e.Provider.ProviderId)
                .Select(g => new ProviderSettingsViewModel(g.ToList(), services)));
        PluginProblems = services.PluginResults.Where(r => !r.Success)
            .Select(r => $"{Path.GetFileName(r.Folder)}: {r.Error}").ToList();
    }

    public string Header => "Preferences";
    public ObservableCollection<ProviderSettingsViewModel> Providers { get; }
    public IReadOnlyList<string> PluginProblems { get; }
    public bool HasPluginProblems => PluginProblems.Count > 0;

    /// <summary>Empty = rename in place; otherwise files are moved under this root following the format's folders.</summary>
    public string LibraryRoot
    {
        get => _services.AppSettings.LibraryRoot;
        set
        {
            _services.AppSettings.LibraryRoot = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private string _cacheStatus = "";

    [RelayCommand]
    private void ClearCache()
    {
        _services.ClearCache();
        CacheStatus = "Cache cleared.";
    }
}
