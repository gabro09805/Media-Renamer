using Avalonia.Media.Imaging;

namespace MediaRenamer.App.Services;

/// <summary>Downloads and caches cover images shared by every window. A failed download is cached as "no cover".</summary>
public static class CoverCache
{
    private static readonly HttpClient Http = new();
    private static readonly Dictionary<Uri, Task<Bitmap?>> Covers = new();
    private static readonly SemaphoreSlim Gate = new(4);

    public static Task<Bitmap?> GetAsync(Uri uri)
    {
        lock (Covers)
        {
            if (!Covers.TryGetValue(uri, out var task)) Covers[uri] = task = DownloadAsync(uri);
            return task;
        }
    }

    private static async Task<Bitmap?> DownloadAsync(Uri uri)
    {
        await Gate.WaitAsync();
        try
        {
            var bytes = await Http.GetByteArrayAsync(uri);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch (Exception)
        {
            // A missing cover is not an error: the UI just shows no image.
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }
}
