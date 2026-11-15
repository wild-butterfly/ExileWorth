using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExileWorth.Windows;

internal sealed record Shortcut(uint Modifiers, Keys Key)
{
    public static readonly Shortcut Default = new(6, Keys.D);
    public static readonly uint[] ModifierOptions = [3, 6, 5, 7];
    public static readonly Keys[] KeyOptions = [
        ..Enumerable.Range((int)Keys.A, 26).Select(key => (Keys)key).Where(key => key != Keys.C),
        ..Enumerable.Range((int)Keys.F1, 12).Select(key => (Keys)key),
    ];

    public static string FormatModifiers(uint value) => value switch
    {
        3 => "Ctrl + Alt",
        6 => "Ctrl + Shift",
        5 => "Alt + Shift",
        _ => "Ctrl + Alt + Shift",
    };

    public override string ToString() => $"{FormatModifiers(Modifiers)} + {Key}";
}

internal sealed record DesktopPreferences(Shortcut Shortcut, bool NearCursor, string? ThanksMessage = null,
    string[]? QuickReplies = null, string? Theme = null, bool? AdvancedItemCopy = null)
{
    public const string DefaultThanks = "Thanks, have a nice day!";

    /// <summary>The macOS defaults (packages/shared-types settings).</summary>
    public static readonly string[] DefaultReplies =
    [
        "Hi, I am ready to trade. Sending an invite now.",
        "Hi, one moment please.",
        "Sorry, that one is already sold.",
    ];

    [JsonIgnore]
    public string[] Replies => QuickReplies ?? DefaultReplies;

    // Records compare arrays by reference; compare the replies by content.
    public bool Equals(DesktopPreferences? other) =>
        other is not null && Shortcut == other.Shortcut && NearCursor == other.NearCursor &&
        ThanksMessage == other.ThanksMessage && Theme == other.Theme && AdvancedItemCopy == other.AdvancedItemCopy &&
        Replies.SequenceEqual(other.Replies);

    public override int GetHashCode() => HashCode.Combine(Shortcut, NearCursor, ThanksMessage, Theme, AdvancedItemCopy);
    [JsonIgnore]
    public string Thanks => string.IsNullOrWhiteSpace(ThanksMessage) ? DefaultThanks : ThanksMessage.Trim();

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Vaaluation", "desktop-settings.json");
    public static readonly DesktopPreferences Default = new(Shortcut.Default, true, AdvancedItemCopy: false);

    public static DesktopPreferences Load()
    {
        try
        {
            var saved = JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(FilePath));
            if (saved?.Shortcut is { } shortcut && Shortcut.ModifierOptions.Contains(shortcut.Modifiers)
                && Shortcut.KeyOptions.Contains(shortcut.Key))
            {
                // Older settings had an opt-in to plain copy. Adopt the new
                // default once, while retaining custom shortcuts and preferences.
                if (saved.AdvancedItemCopy is null)
                    return saved with
                    {
                        AdvancedItemCopy = false,
                        Shortcut = shortcut == new Shortcut(3, Keys.D) ? Shortcut.Default : shortcut,
                    };
                return saved;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        return Default;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
