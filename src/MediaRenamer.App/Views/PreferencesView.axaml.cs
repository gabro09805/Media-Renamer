using Avalonia.Controls;

namespace MediaRenamer.App.Views;

public partial class PreferencesView : UserControl
{
    public PreferencesView() => InitializeComponent();

    private async void OnBrowseLibrary(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.PreferencesViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = "Library root folder" });
        if (folders.Count > 0) vm.LibraryRoot = folders[0].Path.LocalPath;
    }
}
