using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using MediaRenamer.Model;
using MediaRenamer.Plugin.Abstractions;

namespace MediaRenamer.Providers.AniDb;

internal static class AniDbConfig
{
    public const string ClientName = "clientName";
    public const string ClientVersion = "clientVersion";

    public static readonly IReadOnlyList<ConfigField> Schema = new[]
    {
        new ConfigField(ClientName, "Client name (registered on anidb.net)", ConfigFieldType.Text, Required: true),
        new ConfigField(ClientVersion, "Client version", ConfigFieldType.Number, Required: true, DefaultValue: "1"),
    };
}

internal sealed record AniDbTitle(string Type, string Language, string Text, string Normalized);

internal sealed record AniDbEntry(int Aid, IReadOnlyList<AniDbTitle> Titles)
{
    public string Main => Titles.FirstOrDefault(t => t.Type == "main")?.Text ?? Titles[0].Text;
}

/// <summary>
/// AniDB has no search endpoint: names are looked up in the official title dump (downloaded at most once a day,
/// as AniDB requires) and loaded into memory.
/// </summary>
internal sealed class AniDbTitleIndex
{
    public const string DumpUrl = "https://anidb.net/api/anime-titles.xml.gz";
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private readonly HttpClient _http;
    private readonly string _file;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<AniDbEntry>? _entries;

    public AniDbTitleIndex(HttpClient http, string dataDirectory)
    {
        _http = http;
        _file = Path.Combine(dataDirectory, "anime-titles.xml");
    }

    public async Task<IReadOnlyList<AniDbEntry>> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var fresh = File.Exists(_file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(_file) < MaxAge;
            if (_entries is not null && fresh) return _entries;

            if (!fresh)
            {
                try
                {
                    await DownloadAsync(ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    if (!File.Exists(_file))
                        throw new ProviderException(ProviderErrorKind.Network, "Cannot download the AniDB title dump: " + ex.Message, ex);
                    // keep using the stale local dump
                }
            }

            _entries = await Task.Run(() => Parse(File.ReadAllBytes(_file)), ct);
            return _entries;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DownloadAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync(DumpUrl, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"AniDB title dump returned HTTP {(int)response.StatusCode}.");
        var bytes = Decompress(await response.Content.ReadAsByteArrayAsync(ct));
        var tmp = _file + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes, ct);
        File.Move(tmp, _file, overwrite: true);
    }

    internal static byte[] Decompress(byte[] data)
    {
        if (data.Length < 2 || data[0] != 0x1F || data[1] != 0x8B) return data;
        using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        return ms.ToArray();
    }

    internal static IReadOnlyList<AniDbEntry> Parse(byte[] xml)
    {
        var entries = new List<AniDbEntry>();
        using var reader = XmlReader.Create(new MemoryStream(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        int aid = 0;
        List<AniDbTitle>? titles = null;

        var advance = true;
        while (!advance || reader.Read())
        {
            // ReadElementContentAsString already moves to the next node.
            advance = true;
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "anime")
            {
                aid = int.TryParse(reader.GetAttribute("aid"), out var a) ? a : 0;
                titles = new();
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.Name == "title" && titles is not null)
            {
                var type = reader.GetAttribute("type") ?? "";
                var lang = reader.GetAttribute("lang", "http://www.w3.org/XML/1998/namespace") ?? "";
                var text = reader.ReadElementContentAsString();
                if (text.Length > 0) titles.Add(new AniDbTitle(type, lang, text, TextNormalizer.Normalize(text)));
                advance = false;
                if (reader.EOF) break;
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "anime" && titles is { Count: > 0 })
            {
                entries.Add(new AniDbEntry(aid, titles));
                titles = null;
            }
        }
        return entries;
    }

    public static IReadOnlyList<(AniDbEntry Entry, double Score)> Search(IReadOnlyList<AniDbEntry> entries, string query, int max)
    {
        var q = TextNormalizer.Normalize(query);
        if (q.Length == 0) return Array.Empty<(AniDbEntry, double)>();
        var qTokens = q.Split(' ');

        return entries
            .Select(e => (Entry: e, Score: e.Titles.Max(t => Score(t.Normalized, q, qTokens))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Main, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    private static double Score(string title, string q, string[] qTokens)
    {
        if (title == q) return 1.0;
        if (title.StartsWith(q + " ", StringComparison.Ordinal)) return 0.9;
        if (title.Contains(" " + q + " ") || title.EndsWith(" " + q, StringComparison.Ordinal)) return 0.75;
        if (title.Contains(q, StringComparison.Ordinal)) return 0.6;
        var tt = title.Split(' ');
        return qTokens.All(tt.Contains) ? 0.5 : 0;
    }
}

internal static class TextNormalizer
{
    /// <summary>Lower-case, no diacritics, punctuation collapsed to single spaces.</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
