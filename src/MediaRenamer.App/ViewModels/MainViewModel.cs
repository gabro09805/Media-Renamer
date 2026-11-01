using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaRenamer.App.Services;

namespace MediaRenamer.App.ViewModels;

/// <summary>Sidebar entry. The sidebar must contain exclusively Rename, Search Episodes and Preferences.</summary>
public sealed record NavItem(string Title, ViewModelBase Page)
{
    public Avalonia.Media.Geometry IconData => Controls.Ic.GeometryOf(Title switch
    {
        "Rename" => "edit",
        "Search Episodes" => "search",
        _ => "settings",
    });
}

public partial class MainViewModel : ViewModelBase
{
    public Avalonia.Media.Geometry BrandIcon { get; } = Controls.Ic.GeometryOf("film");

    public ObservableCollection<NavItem> NavItems { get; }

    [ObservableProperty]
    private NavItem _selected;

    public MainViewModel(AppServices services)
    {
        var rename = new RenameViewModel(services);
        var search = new SearchEpisodesViewModel(services, rename);
        NavItems = new ObservableCollection<NavItem>
        {
            new("Rename", rename),
            new("Search Episodes", search),
            new("Preferences", new PreferencesViewModel(services)),
        };
        _selected = NavItems[0];
        search.SentToRename += () => Selected = NavItems[0];
    }
}
