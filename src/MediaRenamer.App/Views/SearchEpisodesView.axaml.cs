using Avalonia.Controls;
using Avalonia.Input;
using MediaRenamer.App.ViewModels;

namespace MediaRenamer.App.Views;

public partial class SearchEpisodesView : UserControl
{
    public SearchEpisodesView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Vm?.SuggestQuery();
    }

    private SearchEpisodesViewModel? Vm => DataContext as SearchEpisodesViewModel;

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Vm?.SearchCommand.Execute(null);
    }

    private async void OnSendToRename(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Vm?.CreateSendDialog() is not { } dialog || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (await new SendToRenameWindow(dialog).ShowDialog<bool>(owner)) Vm.Send(dialog);
    }
}
