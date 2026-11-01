using Avalonia.Controls;
using Avalonia.Interactivity;
using MediaRenamer.App.ViewModels;
using MediaRenamer.Model;

namespace MediaRenamer.App.Views;

public partial class MatchPreferencesWindow : Window
{
    public MatchPreferencesWindow()
    {
        InitializeComponent();
        Services.WindowPlacement.Attach(this);
    }

    public MatchPreferencesWindow(MatchPreferences current) : this() =>
        DataContext = new MatchPreferencesViewModel(current);

    private void OnOk(object? sender, RoutedEventArgs e) =>
        Close((DataContext as MatchPreferencesViewModel)?.ToPreferences());

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
