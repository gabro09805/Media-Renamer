using System.Text;

namespace MediaRenamer.Core.Import;

/// <summary>
/// Reads the embedded title of a video container (MKV Segment Info title, MP4/M4V "©nam" tag) with plain C#.
/// Only used when the file name carries no usable title. Never throws: unreadable files return null.
/// </summary>
public static class ContainerTitleReader
{
    public static string? ReadTitle(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var title = ext is ".mkv" or ".webm" ? Mkv(fs) : ext is ".mp4" or ".m4v" or ".mov" ? Mp4(fs, 0, fs.Length, 0) : null;
            title = title?.Trim('\0', ' ');
            return string.IsNullOrWhiteSpace(title) ? null : title;
        }
        catch { return null; }
    }

    // ---------- Matroska ----------

    private const uint IdEbml = 0x1A45DFA3, IdSegment = 0x18538067, IdInfo = 0x1549A966, IdTitle = 0x7BA9, IdCluster = 0x1F43B675;

    private static string? Mkv(Stream s)
    {
        if (ReadId(s) != IdEbml) return null;
        s.Seek(ReadSize(s) ?? 0, SeekOrigin.Current);
        if (ReadId(s) != IdSegment) return null;
        var segSize = ReadSize(s);
        var end = segSize is { } n ? Math.Min(s.Position + n, s.Length) : s.Length;

        while (s.Position < end)
        {
            var id = ReadId(s);
            var size = ReadSize(s);
            if (id is null || size is null || id == IdCluster) return null;
            var next = s.Position + size.Value;
            if (id == IdInfo)
            {
                while (s.Position < next)
                {
                    var cid = ReadId(s);
                    var csize = ReadSize(s);
                    if (cid is null || csize is null) return null;
                    if (cid == IdTitle && csize < 4096)
                    {
                        var buf = new byte[csize.Value];
                        s.ReadExactly(buf);
                        return Encoding.UTF8.GetString(buf);
                    }
                    s.Seek(csize.Value, SeekOrigin.Current);
                }
                return null;
            }
            s.Seek(next, SeekOrigin.Begin);
        }
        return null;
    }

    private static uint? ReadId(Stream s)
    {
        var b = s.ReadByte();
        if (b < 0) return null;
        var len = 1;
        for (var mask = 0x80; len <= 4 && (b & mask) == 0; mask >>= 1) len++;
        if (len > 4) return null;
        var id = (uint)b;
        for (var i = 1; i < len; i++)
        {
            var x = s.ReadByte();
            if (x < 0) return null;
            id = (id << 8) | (uint)x;
        }
        return id;
    }

    /// <summary>EBML variable-length size; null for "unknown size".</summary>
    private static long? ReadSize(Stream s)
    {
        var b = s.ReadByte();
        if (b < 0) return null;
        var len = 1;
        for (var mask = 0x80; len <= 8 && (b & mask) == 0; mask >>= 1) len++;
        if (len > 8) return null;
        var value = (long)(b & (0xFF >> len));
        var allOnes = value == (0xFF >> len);
        for (var i = 1; i < len; i++)
        {
            var x = s.ReadByte();
            if (x < 0) return null;
            allOnes &= x == 0xFF;
            value = (value << 8) | (uint)x;
        }
        return allOnes ? null : value;
    }

    // ---------- MP4 ----------

    private static string? Mp4(Stream s, long start, long end, int depth)
    {
        var pos = start;
        var header = new byte[8];
        while (pos + 8 <= end)
        {
            s.Seek(pos, SeekOrigin.Begin);
            s.ReadExactly(header);
            long size = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            var type = Encoding.Latin1.GetString(header, 4, 4);
            var bodyStart = pos + 8;
            if (size == 1)
            {
                var ext = new byte[8];
                s.ReadExactly(ext);
                size = 0;
                foreach (var x in ext) size = (size << 8) | x;
                bodyStart += 8;
            }
            else if (size == 0) size = end - pos;
            if (size < 8 || pos + size > end) return null;
            var bodyEnd = pos + size;

            switch (type)
            {
                case "moov" or "udta" or "ilst" when depth < 6:
                    if (Mp4(s, bodyStart, bodyEnd, depth + 1) is { } t1) return t1;
                    break;
                case "meta" when depth < 6:
                    if (Mp4(s, bodyStart + 4, bodyEnd, depth + 1) is { } t2) return t2;
                    break;
                case "\u00A9nam":
                    return DataText(s, bodyStart, bodyEnd);
            }
            pos = bodyEnd;
        }
        return null;
    }

    private static string? DataText(Stream s, long start, long end)
    {
        if (end - start < 16 || end - start > 4096 + 16) return null;
        var buf = new byte[end - start];
        s.Seek(start, SeekOrigin.Begin);
        s.ReadExactly(buf);
        return Encoding.Latin1.GetString(buf, 4, 4) == "data" ? Encoding.UTF8.GetString(buf, 16, buf.Length - 16) : null;
    }
}
