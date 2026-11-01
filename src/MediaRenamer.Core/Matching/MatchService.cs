using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Core.Matching;

/// <summary>
/// Matches imported videos against the single provider chosen by the user (no automatic fallback).
/// Series data is fetched once per series and reused for every episode file.
/// </summary>
/// <summary>Asks the user which candidate is right for <paramref name="title"/>; returns null to skip.</summary>
public delegate Task<SearchResult?> ChooseCandidate(string title, IReadOnlyList<string> files, IReadOnlyList<SearchResult> candidates, CancellationToken ct);

public sealed class MatchService
{
    private sealed record SeriesData(Series Series, IReadOnlyList<Episode> Episodes, double Score);

    public async Task<IReadOnlyList<MatchResult>> MatchAsync(
        IReadOnlyList<MediaFile> videos,
        ProviderMode mode,
        IMovieProvider? movieProvider,
        IEpisodeProvider? episodeProvider,
        IProviderConfig movieConfig,
        IProviderConfig episodeConfig,
        MatchPreferences prefs,
        CancellationToken ct = default,
        ChooseCandidate? choose = null)
    {
        var seriesCache = new Dictionary<string, SeriesData?>();
        var results = new List<MatchResult>();
        var minScore = prefs.Mode == MatchMode.Strict ? 0.85 : 0.55;
        var sameTitle = videos.GroupBy(v => TitleSimilarity.Normalize(v.Detected.Title ?? "") + "|" + v.Detected.Year)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(v => System.IO.Path.GetFileName(v.Path)).ToList());

        foreach (var file in videos)
        {
            ct.ThrowIfCancellationRequested();
            var d = file.Detected;
            // The category chosen by the user decides Movie vs TV Series; the file name never does.
            var isEpisode = mode == ProviderMode.Episode;
            var original = isEpisode ? new Numbering(d.Season, d.Episodes.FirstOrDefault() is var e and > 0 ? e : null, d.Absolute) : null;

            try
            {
                results.Add(isEpisode
                    ? await MatchEpisodeAsync(file, episodeProvider, episodeConfig, prefs, minScore, seriesCache, original, choose, sameTitle, ct)
                    : await MatchMovieAsync(file, movieProvider, movieConfig, prefs, minScore, choose, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(Failed(file, original, "Provider error: " + ex.Message));
            }
        }
        return results;
    }

    /// <summary>
    /// Re-reads the already matched series and movies in the new output language, keeping every match (same series, same episode).
    /// Cached provider responses make this cheap; a failed refresh keeps the previous match untouched.
    /// </summary>
    public async Task<IReadOnlyList<MatchResult>> RelocalizeAsync(
        IReadOnlyList<MatchResult> matches,
        IMovieProvider? movieProvider,
        IEpisodeProvider? episodeProvider,
        IProviderConfig movieConfig,
        IProviderConfig episodeConfig,
        MatchPreferences prefs,
        CancellationToken ct = default)
    {
        var series = new Dictionary<ProviderRef, SeriesData?>();
        var result = new List<MatchResult>();
        foreach (var m in matches)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (m.Movie is not null && movieProvider is not null && m.Movie.Source.ProviderId == movieProvider.ProviderId)
                {
                    var movie = await movieProvider.GetMovieAsync(m.Movie.Source, prefs.OutputLanguage, movieConfig, ct);
                    result.Add(m with { Movie = movie });
                }
                else if (m.Series is not null && episodeProvider is not null && m.Series.Source.ProviderId == episodeProvider.ProviderId)
                {
                    var key = m.Series.Source;
                    if (!series.TryGetValue(key, out var data))
                    {
                        var s = await episodeProvider.GetSeriesAsync(key, prefs.OutputLanguage, episodeConfig, ct);
                        var eps = await episodeProvider.GetEpisodesAsync(key, LanguageTag.WithEnglish(prefs.OutputLanguage), episodeConfig, ct);
                        series[key] = data = new SeriesData(s, eps, m.Score);
                    }
                    var episode = m.Episode is null ? null
                        : data!.Episodes.FirstOrDefault(e => e.Number == m.Episode.Number && e.Source.Id == m.Episode.Source.Id) ?? m.Episode;
                    result.Add(m with { Series = data!.Series, Episode = episode });
                }
                else result.Add(m);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Add(m);
            }
        }
        return result;
    }
    /// <summary>
    /// Identifies an episode from the episode title in the file name, comparing every language variant held by the model
    /// (original, English, localized, aliases). Only a clear, unique winner is accepted.
    /// </summary>
    public static Episode? FindEpisodeByTitle(IReadOnlyList<Episode> episodes, DetectedInfo d, out string how)
    {
        how = "No usable episode title in the file name.";
        var title = d.EpisodeTitle;
        if (string.IsNullOrWhiteSpace(title) || TitleSimilarity.Normalize(title).Length < 3) return null;

        var scored = episodes.Select(e => (Episode: e, Score: TitleSimilarity.BestScore(title, e.Titles.AllForMatching())))
            .OrderByDescending(x => x.Score).ToList();
        if (scored.Count == 0 || scored[0].Score < 0.85)
        {
            how = $"Episode title \"{title}\" not found.";
            return null;
        }
        if (scored.Count > 1 && scored[1].Score >= scored[0].Score - 0.03)
        {
            how = $"Episode title \"{title}\" is ambiguous.";
            return null;
        }
        how = $"Episode identified by its title \"{title}\" ({scored[0].Score:P0}).";
        return scored[0].Episode;
    }

    /// <summary>
    /// "Apply to all" for series: the files of the corrected series are re-associated with <paramref name="episodes"/> of the new series,
    /// episode by episode (numbering, then title); the distinction between seasons and episodes is never flattened.
    /// </summary>
    public static IReadOnlyList<MatchResult> ReapplySeries(IReadOnlyList<MatchResult> matches, MatchResult previous, MatchResult edited, Series series,
        IReadOnlyList<Episode> episodes, MatchPreferences prefs)
    {
        var result = new List<MatchResult>(matches.Count);
        foreach (var m in matches)
        {
            if (SamePath(m, edited)) { result.Add(edited); continue; }
            if (!BelongsToSameWork(m, previous)) { result.Add(m); continue; }

            var d = m.File.Detected;
            var original = m.OriginalNumbering ?? new Numbering(d.Season, d.Episodes.FirstOrDefault() is var e and > 0 ? e : null, d.Absolute);
            var episode = FindEpisode(episodes, d, prefs, out var how) ?? FindEpisodeByTitle(episodes, d, out how);
            var more = episode is null ? Array.Empty<Episode>() : FindMoreEpisodes(episodes, d, episode, prefs);
            result.Add(episode is null
                ? new MatchResult
                {
                    File = m.File, Series = series, Confidence = MatchConfidence.Uncertain, Association = AssociationMode.Manual,
                    OriginalNumbering = original, Reasons = new[] { "Series corrected manually. " + how },
                }
                : Manual(m.File, series, episode, null, original) with { MoreEpisodes = more, Reasons = new[] { "Series corrected manually. " + how } });
        }
        return result;
    }

    /// <summary>"Apply to all" for movies: every file of the same movie (or unidentified with the same title) gets the chosen movie.</summary>
    public static IReadOnlyList<MatchResult> ReapplyMovie(IReadOnlyList<MatchResult> matches, MatchResult previous, MatchResult edited, Movie movie) =>
        matches.Select(m => SamePath(m, edited) ? edited
            : BelongsToSameWork(m, previous) ? Manual(m.File, null, null, movie, m.OriginalNumbering)
            : m).ToList();

    private static bool SamePath(MatchResult a, MatchResult b) =>
        string.Equals(a.File.Path, b.File.Path, StringComparison.OrdinalIgnoreCase);

    /// <summary>Same current series/movie as the file being corrected, or still unidentified with the same detected title.</summary>
    public static bool BelongsToSameWork(MatchResult candidate, MatchResult reference)
    {
        var a = (MediaEntity?)candidate.Movie ?? candidate.Series;
        var b = (MediaEntity?)reference.Movie ?? reference.Series;
        if (a is not null && b is not null) return a.Source == b.Source;
        if (a is not null || b is not null) return false;
        var t1 = TitleSimilarity.Normalize(candidate.File.Detected.Title ?? "");
        return t1.Length > 0 && t1 == TitleSimilarity.Normalize(reference.File.Detected.Title ?? "");
    }
    /// <summary>Match chosen by the user (Edit Match dialog): always trusted, and the file-name numbering is kept.</summary>
    public static MatchResult Manual(MediaFile file, Series? series, Episode? episode, Movie? movie, Numbering? original) => new()
    {
        File = file,
        Series = series,
        Episode = episode,
        Movie = movie,
        Score = 1,
        Confidence = MatchConfidence.Reliable,
        Reasons = new[] { "Chosen manually." },
        Association = AssociationMode.Manual,
        OriginalNumbering = original,
    };

    private static MatchResult Failed(MediaFile file, Numbering? original, string reason) => new()
    {
        File = file,
        Confidence = MatchConfidence.None,
        Reasons = new[] { reason },
        OriginalNumbering = original,
    };

    /// <summary>Splits run-together words ("OnePiece" -> "One Piece"); null when nothing changes.</summary>
    public static string? SplitJoinedWords(string title)
    {
        var split = System.Text.RegularExpressions.Regex.Replace(title, @"(?<=\p{Ll})(?=\p{Lu})", " ");
        return split == title ? null : split;
    }

    /// <summary>Searches the title as written and, if it has run-together words, the split form too; hits are merged without duplicates.</summary>
    private static async Task<IReadOnlyList<SearchResult>> SearchVariantsAsync(string title, Func<string, Task<IReadOnlyList<SearchResult>>> search)
    {
        var hits = (await search(title)).ToList();
        if (SplitJoinedWords(title) is { } alt)
            foreach (var h in await search(alt))
                if (!hits.Any(x => x.Ref == h.Ref)) hits.Add(h);
        return hits;
    }

    private static async Task<MatchResult> MatchMovieAsync(MediaFile file, IMovieProvider? provider, IProviderConfig config,
        MatchPreferences prefs, double minScore, ChooseCandidate? choose, CancellationToken ct)
    {
        if (provider is null) return Failed(file, null, "No movie provider enabled/selected.");
        var title = file.Detected.Title;
        if (string.IsNullOrWhiteSpace(title)) return Failed(file, null, "No title detected in the file name.");

        var hits = await SearchVariantsAsync(title, q => provider.SearchMovieAsync(new SearchQuery(q, file.Detected.Year, prefs.OutputLanguage), config, ct));
        var (hit, score, byUser) = await PickAsync(Rank(hits, title, file.Detected.Year).ToList(), minScore, title, new[] { System.IO.Path.GetFileName(file.Path) }, choose, ct);
        if (hit is null)
            return Failed(file, null, $"No movie found for \"{title}\".");

        var movie = await provider.GetMovieAsync(hit.Ref, prefs.OutputLanguage, config, ct);
        return new MatchResult
        {
            File = file,
            Movie = movie,
            Score = score,
            Confidence = byUser ? MatchConfidence.Reliable : ConfidenceOf(score, ambiguous: false),
            Reasons = new[] { byUser ? "Chosen from the candidates." : $"Title match {score:P0}" + (file.Detected.Year is null ? "" : $", year {file.Detected.Year}") },
            Association = byUser ? AssociationMode.Manual : AssociationMode.Auto,
        };
    }

    private static async Task<MatchResult> MatchEpisodeAsync(MediaFile file, IEpisodeProvider? provider, IProviderConfig config,
        MatchPreferences prefs, double minScore, Dictionary<string, SeriesData?> cache, Numbering? original, ChooseCandidate? choose,
        IReadOnlyDictionary<string, IReadOnlyList<string>> sameTitle, CancellationToken ct)
    {
        if (provider is null) return Failed(file, original, "No series provider enabled/selected.");
        var title = file.Detected.Title;
        if (string.IsNullOrWhiteSpace(title)) return Failed(file, original, "No title detected in the file name.");

        var key = TitleSimilarity.Normalize(title) + "|" + file.Detected.Year;
        if (!cache.TryGetValue(key, out var data))
        {
            var hits = await SearchVariantsAsync(title, q => provider.SearchSeriesAsync(new SearchQuery(q, file.Detected.Year, prefs.OutputLanguage), config, ct));
            var files = sameTitle.TryGetValue(key, out var group) ? group : new[] { System.IO.Path.GetFileName(file.Path) };
            var (hit, score, _) = await PickAsync(Rank(hits, title, file.Detected.Year).ToList(), minScore, title, files, choose, ct);
            if (hit is null) data = null;
            else
            {
                var series = await provider.GetSeriesAsync(hit.Ref, prefs.OutputLanguage, config, ct);
                var eps = await provider.GetEpisodesAsync(hit.Ref, LanguageTag.WithEnglish(prefs.OutputLanguage), config, ct);
                data = new SeriesData(series, eps, score);
            }
            cache[key] = data;
        }

        if (data is null) return Failed(file, original, $"No series found for \"{title}\".");

        var episode = FindEpisode(data.Episodes, file.Detected, prefs, out var how) ?? FindEpisodeByTitle(data.Episodes, file.Detected, out how);
        var reasons = new List<string> { $"Series match {data.Score:P0}" };

        if (episode is null)
        {
            reasons.Add(how);
            return new MatchResult
            {
                File = file, Series = data.Series, Score = data.Score, Confidence = MatchConfidence.Uncertain,
                Reasons = reasons, Association = AssociationMode.Auto, OriginalNumbering = original,
            };
        }

        reasons.Add(how);
        var more = FindMoreEpisodes(data.Episodes, file.Detected, episode, prefs);
        if (file.Detected.Episodes.Count > 1)
            reasons.Add(more.Count == file.Detected.Episodes.Count - 1 ? $"Multi-episode file: {more.Count + 1} episodes matched." : "Multi-episode file: some episodes were not found.");
        return new MatchResult
        {
            File = file, Series = data.Series, Episode = episode, MoreEpisodes = more, Score = data.Score,
            Confidence = ConfidenceOf(data.Score, ambiguous: false),
            Reasons = reasons, Association = AssociationMode.EpisodeNumber, OriginalNumbering = original,
        };
    }

    /// <summary>The episodes after the first of a multi-episode file (S01E01E02), looked up with the same numbering rules.</summary>
    internal static IReadOnlyList<Episode> FindMoreEpisodes(IReadOnlyList<Episode> episodes, DetectedInfo d, Episode first, MatchPreferences prefs)
    {
        var more = new List<Episode>();
        foreach (var n in d.Episodes.Skip(1))
        {
            var one = d with { Episodes = new[] { n }, EpisodeTitle = null };
            if (FindEpisode(episodes, one, prefs, out _) is { } e && e != first && !more.Contains(e)) more.Add(e);
        }
        return more;
    }

    internal static Episode? FindEpisode(IReadOnlyList<Episode> episodes, DetectedInfo d, MatchPreferences prefs, out string how)
    {
        var order = prefs.Order;
        var key = order.ToString().ToLowerInvariant();
        Numbering N(Episode e) => e.Orders.TryGetValue(key, out var n) ? n : e.Number;

        if (d.Season is not null && d.Episodes.Count > 0)
        {
            var hit = episodes.Where(e => N(e).Season == d.Season && N(e).Episode == d.Episodes[0]).ToList();
            if (hit.Count == 1) { how = $"S{d.Season:00}E{d.Episodes[0]:00} found ({order})."; return hit[0]; }
            how = hit.Count > 1 ? "Ambiguous numbering: several episodes share S/E." : $"S{d.Season:00}E{d.Episodes[0]:00} not found in the provider ({order}).";
            return null;
        }

        if (d.Absolute is not null)
        {
            var hit = episodes.Where(e => (e.Orders.TryGetValue("absolute", out var a) ? a.Absolute ?? a.Episode : e.Number.Absolute) == d.Absolute).ToList();
            if (hit.Count == 1) { how = $"Absolute number {d.Absolute} found."; return hit[0]; }
            how = hit.Count > 1 ? "Ambiguous absolute number." : $"Absolute number {d.Absolute} not available from this provider.";
            return null;
        }

        // Episode number alone: only safe when the series has a single season.
        var episodeOnly = d.Episodes.Count > 0 ? d.Episodes[0] : (int?)null;
        var regular = episodes.Where(e => !e.IsSpecial).ToList();
        if (episodeOnly is not null && regular.Select(e => N(e).Season).Distinct().Count() == 1)
        {
            var hit = regular.Where(e => N(e).Episode == episodeOnly).ToList();
            if (hit.Count == 1) { how = $"Episode {episodeOnly} found (single season)."; return hit[0]; }
        }
        // No season in the name: opportunistic mode assumes season 1 (typical for anime), and says so.
        if (episodeOnly is not null && prefs.Mode == MatchMode.Opportunistic)
        {
            var hit = regular.Where(e => N(e).Season == 1 && N(e).Episode == episodeOnly).ToList();
            if (hit.Count == 1) { how = $"No season in the file name: assumed season 1, episode {episodeOnly}."; return hit[0]; }
        }
        how = "Season unknown: set the match manually.";
        return null;
    }

    /// <summary>Takes the best hit when it is clear; otherwise (several close or weak candidates) asks the user, if a chooser is available.</summary>
    private static async Task<(SearchResult? Hit, double Score, bool ByUser)> PickAsync(List<(SearchResult? Hit, double Score)> ranked,
        double minScore, string title, IReadOnlyList<string> files, ChooseCandidate? choose, CancellationToken ct)
    {
        if (ranked.Count == 0 || ranked[0].Hit is null) return (null, 0, false);
        var best = ranked[0];
        var ambiguous = best.Score < 0.9 || (ranked.Count > 1 && ranked[1].Score >= best.Score - 0.03);
        if (choose is not null && ambiguous)
        {
            var picked = await choose(title, files, ranked.Take(15).Select(r => r.Hit!).ToList(), ct);
            return picked is null ? (null, 0, true) : (picked, 1, true);
        }
        return best.Score >= minScore ? (best.Hit, best.Score, false) : (null, 0, false);
    }

    private static IEnumerable<(SearchResult? Hit, double Score)> Rank(IReadOnlyList<SearchResult> hits, string title, int? year) =>
        hits.Select(h =>
        {
            var s = TitleSimilarity.BestScore(title, h.Titles.AllForMatching());
            if (year is not null && h.Year is not null) s = h.Year == year ? Math.Min(1, s + 0.05) : s - 0.1;
            return ((SearchResult?)h, s);
        }).OrderByDescending(x => x.s);

    private static MatchConfidence ConfidenceOf(double score, bool ambiguous) =>
        ambiguous ? MatchConfidence.Uncertain : score >= 0.9 ? MatchConfidence.Reliable : score >= 0.7 ? MatchConfidence.Probable : MatchConfidence.Uncertain;
}
