using System.Text.Json;
using Avalonia;
using Avalonia.Controls;

namespace MediaRenamer.App.Services;

/// <summary>Pure geometry used to keep a window entirely visible (all values are device pixels).</summary>
public static class PlacementMath
{
    /// <summary>
    /// The frame is returned unchanged when it is fully inside the work area. Otherwise the position is reset
    /// (centered in the work area) and, only if it is still too big, the size is reduced to fit.
    /// </summary>
    public static PixelRect Fit(PixelRect frame, PixelRect workArea)
    {
        if (workArea.Contains(frame.TopLeft) && workArea.Contains(new PixelPoint(frame.Right - 1, frame.Bottom - 1))) return frame;

        var width = Math.Min(frame.Width, workArea.Width);
        var height = Math.Min(frame.Height, workArea.Height);
        return new PixelRect(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width, height);
    }

    /// <summary>The work area that overlaps the frame the most, or the first one when the frame is on no screen.</summary>
    public static PixelRect ChooseArea(PixelRect frame, IReadOnlyList<PixelRect> areas)
    {
        var best = areas[0];
        var bestArea = 0L;
        foreach (var a in areas)
        {
            var i = a.Intersect(frame);
            var size = (long)i.Width * i.Height;
            if (size > bestArea) { best = a; bestArea = size; }
        }
        return best;
    }
}

/// <summary>
/// Remembers the size (and maximized state) of every window between runs, and guarantees that a window is never
/// bigger than the screen nor partly outside it. Attach it once in the window constructor.
/// </summary>
public static class WindowPlacement
{
    private sealed record Saved(double Width, double Height, bool Maximized, int? X = null, int? Y = null);

    private static string? _path;
    private static Dictionary<string, Saved>? _all;

    /// <summary>Where the sizes are stored; without it nothing is persisted (the screen fitting still works).</summary>
    public static void Configure(string path)
    {
        _path = path;
        _all = null;
    }

    public static void Attach(Window window, bool rememberPosition = false)
    {
        var key = window.GetType().Name;
        var saved = Read(key);
        if (saved is not null)
        {
            if (window.CanResize && saved.Width >= 100 && saved.Height >= 100)
            {
                window.Width = saved.Width;
                window.Height = saved.Height;
            }
            if (rememberPosition && saved.X is { } x && saved.Y is { } y)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = new PixelPoint(x, y);
            }
            if (window.CanResize && saved.Maximized) window.WindowState = WindowState.Maximized;
        }

        window.Opened += (_, _) => KeepOnScreen(window);
        window.Closing += (_, _) => Remember(window, key, rememberPosition);
    }

    private static void KeepOnScreen(Window window)
    {
        if (window.WindowState != WindowState.Normal || window.Screens is not { } screens || screens.All.Count == 0) return;

        var scale = window.DesktopScaling;
        var size = window.FrameSize ?? window.ClientSize;
        var frame = new PixelRect(window.Position, new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)));
        var area = PlacementMath.ChooseArea(frame, screens.All.Select(s => s.WorkingArea).ToList());
        var fitted = PlacementMath.Fit(frame, area);
        if (fitted == frame) return;

        if (fitted.Width != frame.Width || fitted.Height != frame.Height)
        {
            // The frame includes the title bar and borders, which the client size does not.
            var extraW = size.Width - window.ClientSize.Width;
            var extraH = size.Height - window.ClientSize.Height;
            window.MinWidth = Math.Min(window.MinWidth, fitted.Width / scale);
            window.MinHeight = Math.Min(window.MinHeight, fitted.Height / scale);
            window.Width = Math.Max(1, fitted.Width / scale - extraW);
            window.Height = Math.Max(1, fitted.Height / scale - extraH);
        }
        window.Position = fitted.Position;
    }

    private static void Remember(Window window, string key, bool rememberPosition)
    {
        if (_path is null || window.WindowState == WindowState.Minimized) return;
        var all = Load();
        all.TryGetValue(key, out var previous);

        Saved next;
        if (window.WindowState == WindowState.Maximized)
            next = (previous ?? new Saved(window.Width, window.Height, true)) with { Maximized = true };
        else
            next = new Saved(window.ClientSize.Width, window.ClientSize.Height, false,
                rememberPosition ? window.Position.X : null, rememberPosition ? window.Position.Y : null);

        all[key] = next;
        Write(all);
    }

    private static Saved? Read(string key) => Load().GetValueOrDefault(key);

    private static Dictionary<string, Saved> Load()
    {
        if (_all is not null) return _all;
        try
        {
            _all = _path is not null && File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, Saved>>(File.ReadAllText(_path)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _all = new();
        }
        return _all;
    }

    private static void Write(Dictionary<string, Saved> all)
    {
        try
        {
            File.WriteAllText(_path!, JsonSerializer.Serialize(all));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not remembering the size is acceptable.
        }
    }
}
