using System.Text.RegularExpressions;
using MediaRenamer.Model;

namespace MediaRenamer.Core.Parsing;

/// <summary>Infers title, year, numbering and release tags from a file name (no provider involved).</summary>
public static class FileNameParser
{
    private static readonly Regex SxE = new(@"(?<![a-z0-9])s(?<s>\d{1,2})[ ._-]?e(?<e>\d{1,4})(?:[ ._-]?(?:e|-e?)(?<e2>\d{1,4}))*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SxEAll = new(@"(?:e|-e?)(\d{1,4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NxM = new(@"(?<![a-z0-9])(?<s>\d{1,2})x(?<e>\d{2,3})(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SeasonWord = new(@"(?<![a-z0-9])(?:season|stagione|saison|staffel)[ ._-]?(?<s>\d{1,2})[ ._-]*(?:episode|episodio|ep|e)[ ._-]?(?<e>\d{1,4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EpOnly = new(@"(?<![a-z0-9])(?:episodes?|episodio|ep|e|#)[ ._-]?(?<e>\d{1,4})(?:v\d)?(?:[ ._]?[-~&+][ ._]?(?:episodes?|episodio|ep|e)?[ ._]?(?<e2>\d{1,4})(?:v\d)?)?(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] NoiseTags = { "SUB", "SUBS", "SUBBED", "DUB", "DUBBED", "ITA", "ENG", "JPN", "JAP", "SUBITA", "MULTI", "DUAL" };
    private static readonly Regex Year = new(@"(?<![0-9])(?<y>(?:19|20)\d{2})(?![0-9])", RegexOptions.Compiled);
    private static readonly Regex Brackets = new(@"[\[\(]([^\]\)]*)[\]\)]", RegexOptions.Compiled);
    private static readonly Regex AnimeAbs = new(@"^\s*(?:\[[^\]]*\]\s*)?(?<t>.+?)\s+-\s+(?<n>\d{1,4})(?:v\d)?(?:\s|$|\[|\()", RegexOptions.Compiled);
    private static readonly Regex Resolution = new(@"(?<![0-9])(?<h>2160|1440|1080|720|576|480)[pi]|(?<![0-9])(?<w>\d{3,4})x(?<h2>\d{3,4})(?![0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] Sources = { "BluRay", "BDRip", "BRRip", "WEB-DL", "WEBDL", "WEBRip", "WEB", "HDTV", "DVDRip", "DVD", "HDRip", "REMUX", "CAM", "TS" };
    private static readonly string[] VideoCodecs = { "x264", "x265", "h264", "h265", "HEVC", "AVC", "XviD", "DivX", "VP9", "AV1" };
    private static readonly string[] AudioCodecs = { "DTS-HD", "TrueHD", "Atmos", "EAC3", "DDP", "DD5.1", "AC3", "DTS", "AAC", "FLAC", "MP3", "Opus" };
    private static readonly string[] Editions = { "Extended", "Director's Cut", "Directors Cut", "Unrated", "Remastered", "Theatrical", "IMAX", "Final Cut", "Uncut" };
    private static readonly string[] LanguageTags = { "ITA", "ENG", "JPN", "FRE", "FRA", "GER", "DEU", "SPA", "MULTI", "DUAL" };

    public static (DetectedInfo Info, TechnicalInfo Tech) Parse(string path)
    {
        var raw = Path.GetFileNameWithoutExtension(path);
        var text = raw.Replace('_', ' ');

        int? season = null, absolute = null;
        var episodes = new List<int>();
        string? title = null;
        int cut = -1;

        var m = SxE.Match(text);
        if (m.Success)
        {
            season = int.Parse(m.Groups["s"].Value);
            episodes.Add(int.Parse(m.Groups["e"].Value));
            foreach (Match e in SxEAll.Matches(m.Value.Substring(m.Groups["e"].Index - m.Index + m.Groups["e"].Length)))
                episodes.Add(int.Parse(e.Groups[1].Value));
            cut = m.Index;
        }
        else if ((m = SeasonWord.Match(text)).Success || (m = NxM.Match(text)).Success || (m = EpOnly.Match(text)).Success && m.Index > 0)
        {
            if (m.Groups["s"].Success) season = int.Parse(m.Groups["s"].Value);
            var first = int.Parse(m.Groups["e"].Value);
            episodes.Add(first);
            if (m.Groups["e2"].Success && int.Parse(m.Groups["e2"].Value) is var last && last > first && last - first <= 12)
                episodes.AddRange(Enumerable.Range(first + 1, last - first));
            cut = m.Index;
        }

        string? episodeTitle = null;
        if (cut >= 0)
        {
            title = Clean(text.Substring(0, cut));
            var tail = text.Substring(m.Index + m.Length);
            var tailCut = FirstTagIndex(tail);
            var t = Clean(tailCut >= 0 ? tail.Substring(0, tailCut) : tail);
            if (t.Length > 0) episodeTitle = t;
        }
        else
        {
            var a = AnimeAbs.Match(text);
            if (a.Success)
            {
                title = Clean(a.Groups["t"].Value);
                absolute = int.Parse(a.Groups["n"].Value);
                cut = a.Groups["t"].Index + a.Groups["t"].Length;
            }
        }

        int? year = null;
        var ym = Year.Matches(text).Cast<Match>().Where(x => cut < 0 || x.Index < cut || title is null).ToList();
        if (ym.Count > 0)
        {
            // Last year before the numbering/tags, but a title that is itself a year ("2012") stays a title.
            var pick = ym.Count > 1 ? ym[^1] : ym[0];
            if (pick.Index > 0)
            {
                year = int.Parse(pick.Groups["y"].Value);
                if (title is null) cut = pick.Index;
            }
        }

        if (title is null)
        {
            var end = FirstTagIndex(text);
            if (cut < 0) cut = end;
            else if (end >= 0) cut = Math.Min(cut, end);
            title = Clean(cut > 0 ? text.Substring(0, cut) : text);
        }
        else if (year is not null)
        {
            title = Clean(Regex.Replace(title, @"[\[\(]?" + year + @"[\]\)]?\s*$", ""));
        }

        title = Regex.Replace(title, @"^\s*\[[^\]]*\]\s*", "").Trim();

        var info = new DetectedInfo
        {
            Title = string.IsNullOrWhiteSpace(title) ? Clean(text) : title,
            Year = year,
            Season = season,
            Episodes = episodes,
            Absolute = absolute,
            EpisodeTitle = episodeTitle,
            ReleaseGroup = FindGroup(raw),
            Edition = Editions.FirstOrDefault(e => Contains(text, e)),
            Language = LanguageTags.FirstOrDefault(l => Contains(text, l)),
        };

        return (info, ParseTech(text));
    }

    private static TechnicalInfo ParseTech(string text)
    {
        int? height = null, width = null;
        var r = Resolution.Match(text);
        if (r.Success)
        {
            if (r.Groups["h"].Success) height = int.Parse(r.Groups["h"].Value);
            else { width = int.Parse(r.Groups["w"].Value); height = int.Parse(r.Groups["h2"].Value); }
        }

        var vc = VideoCodecs.FirstOrDefault(c => Contains(text, c));
        return new TechnicalInfo
        {
            Height = height,
            Width = width,
            VideoCodec = vc,
            AudioCodec = AudioCodecs.FirstOrDefault(c => Contains(text, c)),
            Source = Sources.FirstOrDefault(s => Contains(text, s)),
            BitDepth = Contains(text, "10bit") || Contains(text, "10-bit") ? 10 : null,
            Hdr = Contains(text, "HDR10") || Contains(text, "HDR") ? "HDR" : null,
        };
    }

    private static string? FindGroup(string raw)
    {
        var lead = Regex.Match(raw, @"^\[(?<g>[^\]]+)\]");
        if (lead.Success && !Regex.IsMatch(lead.Groups["g"].Value, @"^\d{3,4}[pi]$")) return lead.Groups["g"].Value;
        var tail = Regex.Match(raw, @"-(?<g>[A-Za-z0-9]{2,12})$");
        return tail.Success && !Regex.IsMatch(tail.Groups["g"].Value, @"^\d+$") ? tail.Groups["g"].Value : null;
    }

    private static int FirstTagIndex(string text)
    {
        var best = -1;
        void Consider(int i) { if (i >= 0 && (best < 0 || i < best)) best = i; }

        var r = Resolution.Match(text);
        if (r.Success) Consider(r.Index);
        foreach (var tag in Sources.Concat(VideoCodecs).Concat(AudioCodecs).Concat(Editions))
        {
            var mm = Regex.Match(text, @"(?<![A-Za-z0-9])" + Regex.Escape(tag) + @"(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
            if (mm.Success) Consider(mm.Index);
        }
        foreach (var tag in NoiseTags)
        {
            var mm = Regex.Match(text, @"(?<![A-Za-z0-9])" + tag + @"(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
            if (mm.Success) Consider(mm.Index);
        }
        var br = Brackets.Match(text);
        if (br.Success) Consider(br.Index);
        return best;
    }

    private static bool Contains(string text, string tag) =>
        Regex.IsMatch(text, @"(?<![A-Za-z0-9])" + Regex.Escape(tag).Replace(@"\ ", "[ ._]") + @"(?![A-Za-z0-9])", RegexOptions.IgnoreCase);

    private static string Clean(string s)
    {
        s = s.Replace('.', ' ').Replace('_', ' ');
        s = Regex.Replace(s, @"\s+", " ");
        s = s.Trim(' ', '-');
        if (s.EndsWith('(') || s.EndsWith('[')) s = s[..^1].TrimEnd(' ', '-');
        if (s.StartsWith(')') || s.StartsWith(']')) s = s[1..].TrimStart(' ', '-');
        return s;
    }
}
