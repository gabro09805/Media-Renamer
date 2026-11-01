using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MediaRenamer.Core.Renaming;

namespace MediaRenamer.App.Views;

/// <summary>Lists every rename that is about to happen; nothing is touched until the user confirms.</summary>
public sealed class ConfirmRenameWindow : Window
{
    public ConfirmRenameWindow(IReadOnlyList<PlanItem> plan)
    {
        Title = "Confirm rename";
        Width = 960;
        Height = 560;
        MinWidth = 560;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Services.WindowPlacement.Attach(this);

        var moves = plan.Count(p => p.Target is not null && !SameFolder(p));
        var header = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock { Text = $"{plan.Count} file(s) will be renamed", FontSize = 18, FontWeight = FontWeight.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = moves > 0 ? $"{moves} of them will also be moved to another folder. Nothing is overwritten." : "Files are renamed in place. Nothing is overwritten.",
            Opacity = 0.7,
        });
        DockPanel.SetDock(header, Dock.Top);

        var ok = new Button { Content = $"Rename {plan.Count} file(s)", IsDefault = true, MinWidth = 140, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0), Children = { ok, cancel },
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var list = new ListBox { ItemsSource = plan.Select(BuildRow).ToList() };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        Content = new DockPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                header,
                buttons,
                new Border
                {
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)), Child = list,
                },
            },
        };
    }

    private static bool SameFolder(PlanItem p) =>
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(p.Source)), Path.GetDirectoryName(Path.GetFullPath(p.Target!)), StringComparison.OrdinalIgnoreCase);

    // Text is never wrapped: long names are reached with the horizontal scroll bar.
    private static Control BuildRow(PlanItem p)
    {
        var accent = new SolidColorBrush(Color.FromRgb(0x3B, 0xA5, 0x5C));
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(2, 4) };
        ToolTip.SetTip(panel, $"{p.Source}{Environment.NewLine}→ {p.Target}");

        panel.Children.Add(new TextBlock { Text = (p.IsSubtitle ? "Subtitle · " : "") + Path.GetFileName(p.Source), Opacity = 0.65, TextWrapping = TextWrapping.NoWrap });
        panel.Children.Add(new TextBlock
        {
            Text = "→  " + Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(p.Source))!, p.Target!),
            FontWeight = FontWeight.SemiBold, Foreground = accent, TextWrapping = TextWrapping.NoWrap,
        });
        if (!string.IsNullOrWhiteSpace(p.Message))
            panel.Children.Add(new TextBlock { Text = "⚠ " + p.Message, Foreground = Brushes.Orange, FontSize = 12, TextWrapping = TextWrapping.NoWrap });
        return panel;
    }
}