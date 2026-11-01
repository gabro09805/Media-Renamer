using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace MediaRenamer.App.Controls;

/// <summary>
/// Vector icons (24x24, drawn with a stroke, so they follow the text colour in every button state).
/// <c>ctl:Ic.Icon="name"</c> on a Button puts the icon before its text; with no text the button shows only the icon.
/// </summary>
public static class Ic
{
    public static readonly AttachedProperty<string?> IconProperty =
        AvaloniaProperty.RegisterAttached<Button, Button, string?>("Icon");

    /// <summary>When true the text goes under the icon (narrow buttons).</summary>
    public static readonly AttachedProperty<bool> StackedProperty =
        AvaloniaProperty.RegisterAttached<Button, Button, bool>("Stacked");

    public static bool GetStacked(Button b) => b.GetValue(StackedProperty);
    public static void SetStacked(Button b, bool value) => b.SetValue(StackedProperty, value);

    public static string? GetIcon(Button b) => b.GetValue(IconProperty);
    public static void SetIcon(Button b, string? value) => b.SetValue(IconProperty, value);

    /// <summary>Path data of the available icons.</summary>
    public static readonly IReadOnlyDictionary<string, string> Paths = new Dictionary<string, string>
    {
        ["up"] = "M12 19V5 M5 12l7-7 7 7",
        ["down"] = "M12 5v14 M19 12l-7 7-7-7",
        ["close"] = "M18 6 6 18 M6 6l12 12",
        ["refresh"] = "M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8 M21 3v5h-5",
        ["folder"] = "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z",
        ["search"] = "M21 21l-4.3-4.3 M11 3a8 8 0 1 0 0 16a8 8 0 0 0 0-16",
        ["download"] = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5 M12 15V3",
        ["upload"] = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M17 8l-5-5-5 5 M12 3v12",
        ["edit"] = "M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z",
        ["check"] = "M20 6 9 17l-5-5",
        ["arrow-right"] = "M5 12h14 M12 5l7 7-7 7",
        ["link"] = "M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71 M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71",
        ["undo"] = "M3 7v6h6 M21 17a9 9 0 0 0-9-9 9 9 0 0 0-6 2.3L3 13",
        ["trash"] = "M3 6h18 M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2",
        ["save"] = "M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z M17 21v-8H7v8 M7 3v5h8",
        ["copy"] = "M10 8h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H10a2 2 0 0 1-2-2V10a2 2 0 0 1 2-2z M4 16a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2",
        ["plus"] = "M12 5v14 M5 12h14",
        ["select-all"] = "M9 11l3 3L22 4 M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11",
        ["deselect"] = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z",
        ["expand"] = "M7 6l5 5 5-5 M7 13l5 5 5-5",
        ["collapse"] = "M17 11l-5-5-5 5 M17 18l-5-5-5 5",
        ["send"] = "M22 2 11 13 M22 2l-7 20-4-9-9-4z",
        ["plug"] = "M13 2 3 14h9l-1 8 10-12h-9z",
        ["sliders"] = "M4 21v-7 M4 10V3 M12 21v-9 M12 8V3 M20 21v-5 M20 12V3 M1 14h6 M9 8h6 M17 16h6",
        ["braces"] = "M8 3H7a2 2 0 0 0-2 2v5a2 2 0 0 1-2 2 2 2 0 0 1 2 2v5c0 1.1.9 2 2 2h1 M16 21h1a2 2 0 0 0 2-2v-5c0-1.1.9-2 2-2a2 2 0 0 1-2-2V5a2 2 0 0 0-2-2h-1",
        ["film"] = "M4 3h16a1 1 0 0 1 1 1v16a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M7 3v18 M17 3v18 M3 12h18 M3 7.5h4 M3 16.5h4 M17 7.5h4 M17 16.5h4",
        ["settings"] = "M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z",
        ["tv"] = "M4 7h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2z M17 2l-5 5-5-5",
    };

    static Ic()
    {
        IconProperty.Changed.AddClassHandler<Button>((b, _) => Apply(b));
        StackedProperty.Changed.AddClassHandler<Button>((b, _) => Apply(b));
    }

    private static void Apply(Button b)
    {
        if (GetIcon(b) is not { } name || !Paths.TryGetValue(name, out var data)) return;
        var stacked = GetStacked(b);
        b.Content ??= "";
        b.ContentTemplate = new FuncDataTemplate<object>((content, _) => Build(data, content as string, stacked));
    }

    private static Control Build(string data, string? text, bool stacked = false)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = 1.9,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };
        path.Bind(Shape.StrokeProperty, path.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));

        var panel = new StackPanel { Orientation = stacked ? Orientation.Vertical : Orientation.Horizontal, Spacing = stacked ? 3 : 7, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(path);
        if (!string.IsNullOrEmpty(text))
            panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    public static Geometry GeometryOf(string name) => Geometry.Parse(Paths.GetValueOrDefault(name) ?? "M0 0");
}
