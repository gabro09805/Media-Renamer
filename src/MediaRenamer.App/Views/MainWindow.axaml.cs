using Avalonia.Controls;

namespace MediaRenamer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Services.WindowPlacement.Attach(this, rememberPosition: true);
    }
}