using System.Globalization;
using MediaRenamer.Bindings.Expressions;

namespace MediaRenamer.Bindings.Catalog;

/// <summary>Ready-made library layouts (<c>{plex}</c>, <c>{kodi}</c>, <c>{emby}</c>, <c>{jellyfin}</c>), built only from canonical data.</summary>
internal static class LibraryNaming
{
    public enum Style { Plex, Kodi, Emby, Jellyfin }

    /// <summary>The value of <c>{plex}</c> and friends: it prints as a relative path and exposes <c>.name</c>, <c>.id</c> and (Plex only) <c>.year</c>.</summary>
    public static object? Build(MediaBindingSource m, Style style)
    {
        if (m.SeriesOrMovieName(m.Language) is null) return null;
        return Make(m, style, withYear: false, withId: false, nested: true);
    }

    private static ValueBag Make(MediaBindingSource m, Style style, bool withYear, bool withId, bool nested)
    {
        var path = FullPath(m, style, withYear, withId);
        var props = new Dictionary<string, object?> { ["name"] = new ValueBag(path[(path.LastIndexOf(Path.DirectorySeparatorChar) + 1)..]) };

        if (!withId && IdTag(m, style) is not null) props["id"] = Make(m, style, withYear, true, nested: false);
        if (nested && style == Style.Plex) props["year"] = Make(m, style, true, false, nested: false);

        return new ValueBag(path, props, isPath: true);
    }

    private static string FullPath(MediaBindingSource m, Style style, bool withYear, bool withId)
    {
        var sep = Path.DirectorySeparatorChar;
        var name = m.SeriesOrMovieName(m.Language)!;
        var isEpisode = m.Episode is not null && m.Movie is null;

        // Plex names a series without its year unless asked; every other layout, and every movie, includes it.
        var useYear = withYear || style != Style.Plex || !isEpisode;
        var baseName = useYear && m.Year is { } y ? $"{name} ({y})" : name;

        var tag = withId ? IdTag(m, style) : null;
        var folder = tag is null ? baseName : $"{baseName} {tag}";

        if (!isEpisode) return $"Movies{sep}{folder}{sep}{baseName}";

        var season = m.SeasonNumber is { } s ? s.ToString(style == Style.Kodi ? "0" : "00", CultureInfo.InvariantCulture) : "00";
        return $"TV Shows{sep}{folder}{sep}Season {season}{sep}{EpisodeFileName(m, style, baseName)}";
    }

    private static string EpisodeFileName(MediaBindingSource m, Style style, string baseName)
    {
        string? token = null;
        if (m.SeasonNumber is { } s && m.EpisodeNumber is { } e)
            token = style == Style.Kodi ? $"{s}x{e:00}" : $"S{s:00}E{e:00}";
        var title = m.EpisodeTitleIn(m.Language);
        return string.Join(" - ", new[] { baseName, token, title }.Where(p => !string.IsNullOrEmpty(p)));
    }

    private static string? IdTag(MediaBindingSource m, Style style)
    {
        foreach (var (key, embyKey) in new[] { ("tmdb", "tmdbid"), ("tvdb", "tvdbid"), ("imdb", "imdbid"), ("anidb", "anidbid") })
        {
            if (m.Id(key) is not { } id) continue;
            return style switch
            {
                Style.Plex => $"{{{key}-{id}}}",
                Style.Kodi => $"{{{key}={id}}}",
                Style.Emby => $"[{embyKey}={id}]",
                _ => $"[{embyKey}-{id}]",
            };
        }
        return null;
    }
}
