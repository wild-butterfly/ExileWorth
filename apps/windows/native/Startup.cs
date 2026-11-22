using System.Diagnostics;
using Microsoft.Win32;

namespace ExileWorth.Windows;

/// <summary>
/// The per-user "start when I sign in" entry. ExileWorth cannot start itself
/// when Path of Exile opens, so starting quietly in the tray at sign-in is how
/// the price-check shortcut is ready whenever the game is in front.
/// </summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "ExileWorth";

    private static string Command => $"\"{Application.ExecutablePath}\" --tray";

    public static bool IsEnabled() => RegisteredPath() is not null;

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(Name, Command);
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }

    /// <summary>
    /// Each release is a new ZIP, often extracted to a new folder, which left
    /// the entry starting the old copy. Repoint an existing entry at this copy
    /// when the registered one is gone or older; never downgrade it.
    /// </summary>
    public static void Repair()
    {
        try
        {
            string? registered = RegisteredPath();
            if (registered is null) return;
            string current = Application.ExecutablePath;
            if (string.Equals(Path.GetFullPath(registered), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase))
                return;
            if (File.Exists(registered) && VersionOf(registered) > VersionOf(current)) return;
            SetEnabled(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or ArgumentException or NotSupportedException) { }
    }

    private static string? RegisteredPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(Name) is not string command) return null;
            // "C:\path\ExileWorth.exe" --tray
            command = command.Trim();
            if (command.StartsWith('"'))
            {
                int end = command.IndexOf('"', 1);
                return end > 1 ? command[1..end] : null;
            }
            int space = command.IndexOf(' ');
            return space > 0 ? command[..space] : command;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Version VersionOf(string path)
    {
        string? text = FileVersionInfo.GetVersionInfo(path).ProductVersion?.Split('+')[0];
        return Version.TryParse(text, out var version) ? version : new Version(0, 0);
    }
}
