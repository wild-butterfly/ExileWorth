namespace ExileWorth.Windows;

/// <summary>Reads recent trade whispers from the end of the game's Client.txt.</summary>
internal static class TradeLog
{
    public static async Task<TradeWhisper[]> ReadAsync(Worker worker, string? path, string thanks, string[] replies)
    {
        if (path is null || !File.Exists(path))
            throw new IOException("Start Path of Exile to read trade whispers from its chat log.");
        string[] lines;
        await using (var file = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true))
        {
            long start = Math.Max(0, file.Length - 65536);
            file.Position = start;
            using var reader = new StreamReader(file);
            if (start > 0) await reader.ReadLineAsync();
            lines = (await reader.ReadToEndAsync()).Split('\n');
        }
        return await worker.SendAsync<TradeWhisper[]>(new { action = "trades", lines, thanks, replies });
    }
}
