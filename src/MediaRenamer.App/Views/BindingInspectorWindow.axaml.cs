using Avalonia.Controls;
using Avalonia.Input;
using MediaRenamer.App.ViewModels;

namespace MediaRenamer.App.Views;

public partial class BindingInspectorWindow : Window
{
    public BindingInspectorWindow()
    {
        InitializeComponent();
        Services.WindowPlacement.Attach(this);
        Closed += (_, _) => (DataContext as BindingInspectorViewModel)?.Dispose();
    }

    public BindingInspectorWindow(BindingInspectorViewModel vm) : this() => DataContext = vm;

    private async void OnPick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is BindingInspectorViewModel vm) await new SeriesPickerWindow(vm).ShowDialog(this);
    }
}
