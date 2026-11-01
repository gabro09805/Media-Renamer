using Avalonia.Controls;
using MediaRenamer.App.ViewModels;

namespace MediaRenamer.App.Views;

/// <summary>Closes with true when the user confirms the pairing.</summary>
public partial class SendToRenameWindow : Window
{
    public SendToRenameWindow()
    {
        InitializeComponent();
        Services.WindowPlacement.Attach(this);
    }

    public SendToRenameWindow(SendToRenameViewModel vm) : this() => DataContext = vm;

    private void OnOk(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(false);
}
