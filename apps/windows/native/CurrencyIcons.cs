namespace ExileWorth.Windows;

/// <summary>
/// Currency art from GGG's static trade data, loaded at runtime from their CDN
/// (as on macOS) so the app never redistributes the game's artwork.
/// </summary>
internal static class CurrencyIcons
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Dictionary<string, Image> Images = [];
    private static readonly HashSet<string> Pending = [];
    private static Dictionary<string, string>? urls;
    private static Task? loading;

    /// <summary>Raised on the UI thread whenever another icon becomes available.</summary>
    public static event Action? Changed;

    static CurrencyIcons()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"ExileWorth/{Application.ProductVersion.Split('+')[0]} (+https://github.com/wild-butterfly/ExileWorth)");
    }

    public static Image? Get(string id)
    {
        if (Images.TryGetValue(id, out var image)) return image;
        if (urls is not null && urls.TryGetValue(id, out var url)) _ = DownloadAsync(id, url);
        return null;
    }

    /// <summary>Loads the id → URL catalog once. Safe to call repeatedly; failures retry next call.</summary>
    public static Task LoadAsync(Worker worker, IEnumerable<string>? warm = null)
    {
        if (urls is not null)
        {
            foreach (var id in warm ?? []) Get(id);
            return Task.CompletedTask;
        }
        return loading ??= LoadCatalogAsync(worker, warm);
    }

    private static async Task LoadCatalogAsync(Worker worker, IEnumerable<string>? warm)
    {
        try
        {
            var catalog = await worker.SendAsync<Dictionary<string, string>>(new { action = "icons" });
            urls = catalog
                .Where(entry => Uri.TryCreate(entry.Value, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps && uri.Host == "web.poecdn.com")
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            foreach (var id in warm ?? []) Get(id);
            Changed?.Invoke();
        }
        catch (Exception)
        {
            // Icons are decoration; text labels still identify every currency.
        }
        finally { loading = null; }
    }

    private static async Task DownloadAsync(string id, string url)
    {
        if (!Pending.Add(id)) return;
        try
        {
            byte[] bytes = await Http.GetByteArrayAsync(url);
            using var stream = new MemoryStream(bytes);
            using var source = Image.FromStream(stream);
            Images[id] = new Bitmap(source);
            Changed?.Invoke();
        }
        catch (Exception)
        {
            // Leave the slot empty; Pending stops a retry storm for a broken URL.
        }
    }
}
