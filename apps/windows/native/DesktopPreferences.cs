using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExileWorth.Windows;

internal sealed record Shortcut(uint Modifiers, Keys Key)
{
    public static readonly Shortcut Default = new(1, Keys.D);
    // 1 = Alt alone (the easiest to press); 3 = Ctrl + Alt; 6 = Ctrl + Shift; 5 = Alt + Shift; 7 = all three.
    // Shift alone is never offered: it would fire while typing capitals in chat.
    public static readonly uint[] ModifierOptions = [1, 3, 6, 5, 7];
    public static readonly Keys[] KeyOptions = [
        ..Enumerable.Range((int)Keys.A, 26).Select(key => (Keys)key).Where(key => key != Keys.C),
        ..Enumerable.Range((int)Keys.F1, 12).Select(key => (Keys)key),
    ];

    public static string FormatModifiers(uint value) => value switch
    {
        1 => "Alt",
        3 => "Ctrl + Alt",
        6 => "Ctrl + Shift",
        5 => "Alt + Shift",
        _ => "Ctrl + Alt + Shift",
    };

    /// <summary>Alt + F4 closes the game, so it can never be the price-check shortcut.</summary>
    public static bool IsAllowed(uint modifiers, Keys key) =>
        ModifierOptions.Contains(modifiers) && KeyOptions.Contains(key) && !(modifiers == 1 && key == Keys.F4);

    public bool IsAllowed() => IsAllowed(Modifiers, Key);

    public override string ToString() => $"{FormatModifiers(Modifiers)} + {Key}";
}

internal sealed record DesktopPreferences(Shortcut Shortcut, bool NearCursor, string? ThanksMessage = null,
    string[]? QuickReplies = null, string? Theme = null, bool? AdvancedItemCopy = null,
    bool? StartupPromptAnswered = null, int ShortcutDefaultsVersion = 0)
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
        StartupPromptAnswered == other.StartupPromptAnswered && ShortcutDefaultsVersion == other.ShortcutDefaultsVersion &&
        Replies.SequenceEqual(other.Replies);

    public override int GetHashCode() => HashCode.Combine(Shortcut, NearCursor, ThanksMessage, Theme, AdvancedItemCopy, StartupPromptAnswered, ShortcutDefaultsVersion);
    [JsonIgnore]
    public string Thanks => string.IsNullOrWhiteSpace(ThanksMessage) ? DefaultThanks : ThanksMessage.Trim();

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Vaaluation", "desktop-settings.json");
    public static readonly DesktopPreferences Default = new(Shortcut.Default, true, AdvancedItemCopy: false, ShortcutDefaultsVersion: 1);

    internal static DesktopPreferences Upgrade(DesktopPreferences saved)
    {
        if (saved.AdvancedItemCopy is null)
            saved = saved with
            {
                AdvancedItemCopy = false,
                Shortcut = saved.Shortcut == new Shortcut(3, Keys.D) ? Shortcut.Default : saved.Shortcut,
            };
        // Move the previous default once. A later explicit choice of Ctrl +
        // Shift + D is retained, as are all other custom bindings.
        if (saved.ShortcutDefaultsVersion < 1)
            saved = saved with
            {
                Shortcut = saved.Shortcut == new Shortcut(6, Keys.D) ? Shortcut.Default : saved.Shortcut,
                ShortcutDefaultsVersion = 1,
            };
        return saved;
    }

    public static DesktopPreferences Load()
    {
        try
        {
            var saved = JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(FilePath));
            if (saved?.Shortcut is { } shortcut && shortcut.IsAllowed())
            {
                return Upgrade(saved);
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
