using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ExileWorth.Windows;

internal sealed class Worker : IDisposable
{
    private Process? process;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    // One pipe carries one request at a time; overlapping writes would pair
    // a response with the wrong caller.
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<T> SendAsync<T>(object request, TimeSpan? timeout = null)
    {
        await gate.WaitAsync();
        try { return await SendOnceAsync<T>(request, timeout); }
        finally { gate.Release(); }
    }

    private async Task<T> SendOnceAsync<T>(object request, TimeSpan? timeout)
    {
        process ??= Start();
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, Json));
            string line = await process.StandardOutput.ReadLineAsync().WaitAsync(timeout ?? TimeSpan.FromSeconds(100))
                ?? throw new IOException("The trade engine stopped.");
            using var response = JsonDocument.Parse(line);
            if (response.RootElement.TryGetProperty("error", out var error))
                throw new TradeException(error.GetString() ?? "Trade request failed.");
            return response.RootElement.GetProperty("result").Deserialize<T>(Json)
                ?? throw new IOException("The trade engine returned no result.");
        }
        catch (Exception error) when (error is not TradeException)
        {
            Dispose();
            throw;
        }
    }

    private static Process Start()
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "node.exe"))
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "worker.cjs"));
        // Do not inherit optional Node loaders or hooks from the shell.
        info.Environment.Remove("NODE_OPTIONS");
        info.Environment.Remove("NODE_PATH");
        return Process.Start(info) ?? throw new IOException("Could not start the trade engine.");
    }

    public void Dispose()
    {
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(); }
        catch (InvalidOperationException) { }
        finally { process.Dispose(); process = null; }
    }

    private sealed class TradeException(string message) : Exception(message);
}

internal record League(string Id, string Text)
{
    public override string ToString() => Text;
}
internal record LeagueResult(League[] Leagues, string? Selected);
internal record PriceResult(League[] Leagues, string Selected, int Total, Listing[] Listings,
    Modifier[] Filters, string TradeUrl, string? ItemName, TypicalPrice? Typical);
internal record TypicalPrice(double Amount, string Currency, double? Chaos);
internal record Listing(double Amount, string Currency, string AccountName, int? Ilvl, string Presence,
    string? Indexed = null, string? Whisper = null);
internal record Modifier(string Key, string Label, bool Enabled)
{
    public override string ToString() => Label;
}

internal record CurrencyResult(string Selected, CurrencyRow[] Rows);
internal record CurrencyRow(string Id, string Label, CurrencyQuote? Rate, string? Error);
internal record CurrencyQuote(double Median, int SampleSize);

internal record TradeWhisper(string Character, string Summary, string Direction, string Reply,
    string Invite, string Trade, string Kick, string? ReceivedAt = null, string? Raw = null,
    string[]? Replies = null)
{
    public override string ToString() => $"{(Direction == "incoming" ? "From" : "To")} {Character} · {Summary}";
}
