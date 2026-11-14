using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ExileWorth.Windows;

internal sealed class GameHotkey : IDisposable
{
    public const int Message = 0x0312;
    public const int Id = 1;
    private readonly nint window;
    private readonly WinEvent callback;
    private readonly nint hook;
    private readonly Action<bool> focusChanged;
    private readonly System.Windows.Forms.Timer focusCheck;
    private bool registered;
    private Shortcut shortcut;
    private bool gameActive;

    public GameHotkey(nint window, Shortcut shortcut, Action<bool> focusChanged)
    {
        this.window = window;
        this.shortcut = shortcut;
        this.focusChanged = focusChanged;
        callback = (_, _, _, _, _, _, _) => Sync();
        hook = SetWinEventHook(3, 3, 0, callback, 0, 0, 0);
        // Check once at startup and recover if Windows misses a focus event.
        focusCheck = new() { Interval = 500 };
        focusCheck.Tick += (_, _) => Sync();
        focusCheck.Start();
        Sync();
    }

    private void Sync()
    {
        bool eligible = ForegroundGame() != 0;
        if (gameActive != eligible)
        {
            gameActive = eligible;
            focusChanged(eligible);
        }
        if (eligible && !registered)
        {
            registered = RegisterHotKey(window, Id, 0x4000 | shortcut.Modifiers, (uint)shortcut.Key);
        }
        else if (!eligible && registered)
        {
            UnregisterHotKey(window, Id);
            registered = false;
        }
    }

    public void SetShortcut(Shortcut value)
    {
        if (registered) UnregisterHotKey(window, Id);
        registered = false;
        shortcut = value;
        Sync();
    }

    public static nint ForegroundGame()
    {
        nint active = GetForegroundWindow();
        GetWindowThreadProcessId(active, out uint pid);
        if (pid == 0) return 0;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName.ToLowerInvariant() is "pathofexile" or "pathofexile_x64"
                or "pathofexilesteam" or "pathofexile_x64steam" ? active : 0;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            return 0;
        }
    }

    public static string? ClientLog(nint game)
    {
        GetWindowThreadProcessId(game, out uint pid);
        if (pid == 0) return null;
        using var process = Process.GetProcessById((int)pid);
        string? folder = Path.GetDirectoryName(process.MainModule?.FileName);
        return folder is null ? null : Path.Combine(folder, "logs", "Client.txt");
    }

    public static async Task<string?> CopyItemAsync(nint game, Keys trigger)
    {
        // Never forward a copy command if the game lost focus after WM_HOTKEY.
        if (game == 0 || ForegroundGame() != game) return null;
        // Wait for the shortcut modifiers to be released before sending the
        // game's own Ctrl+Alt+C copy command. This avoids releasing held keys.
        for (int i = 0; i < 25 && (Down(Keys.ControlKey) || Down(Keys.Menu) ||
            Down(Keys.ShiftKey) || Down(Keys.LWin) || Down(Keys.RWin) || Down(trigger)); i++)
            await Task.Delay(20);
        if (ForegroundGame() != game || Down(Keys.ControlKey) || Down(Keys.Menu) ||
            Down(Keys.ShiftKey) || Down(Keys.LWin) || Down(Keys.RWin) || Down(trigger)) return null;
        uint start = GetClipboardSequenceNumber();
        Input[] input = [
            Key(Keys.ControlKey, false), Key(Keys.Menu, false), Key(Keys.C, false),
            Key(Keys.C, true), Key(Keys.Menu, true), Key(Keys.ControlKey, true)
        ];
        if (SendInput((uint)input.Length, input, Marshal.SizeOf<Input>()) != (uint)input.Length)
            throw new InvalidOperationException("Windows blocked the copy command. Run the game and ExileWorth at the same privilege level.");
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(25);
            if (ForegroundGame() != game) return null;
            if (GetClipboardSequenceNumber() != start)
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch (ExternalException) { /* The game may still have the clipboard open. */ }
            }
        }
        throw new InvalidOperationException("No item was copied. Hover an item in the English game client or copy it manually.");
    }

    public static async Task SendReplyAsync(nint game, string reply)
    {
        if (reply.Length < 3 || reply.Length > 240 || reply[0] != '@' || reply.Any(char.IsControl))
            throw new InvalidOperationException("The trade reply is invalid.");

        void SendToGame(Input[] keys)
        {
            if (game == 0 || ForegroundGame() != game)
                throw new InvalidOperationException("Focus Path of Exile before sending a reply.");
            if (SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>()) != (uint)keys.Length)
                throw new InvalidOperationException("Windows blocked game input. Run both apps at the same privilege level.");
        }

        if (Down(Keys.ControlKey) || Down(Keys.Menu) || Down(Keys.ShiftKey) ||
            Down(Keys.LWin) || Down(Keys.RWin))
            throw new InvalidOperationException("Release the modifier keys before sending a reply.");

        var enter = new[] { Key(Keys.Enter, false), Key(Keys.Enter, true) };
        SendToGame(enter);
        // Let the game open chat before typing. Submit only if the original
        // window is still in focus, and never retry a partial send.
        await Task.Delay(90);
        SendToGame(reply.SelectMany(character => new[] { Unicode(character, false), Unicode(character, true) }).ToArray());
        await Task.Delay(50);
        SendToGame(enter);
    }

    private static Input Unicode(char character, bool up) => new()
    {
        Type = 1,
        Data = new InputData { Keyboard = new KeyboardInput { Scan = character, Flags = up ? 6u : 4u } }
    };

    private static bool Down(Keys key) => GetAsyncKeyState((int)key) < 0;

    private static Input Key(Keys key, bool up) => new()
    {
        Type = 1,
        Data = new InputData { Keyboard = new KeyboardInput { Key = (ushort)key, Flags = up ? 2u : 0u } }
    };

    public void Dispose()
    {
        focusCheck.Dispose();
        if (hook != 0) UnhookWinEvent(hook);
        if (registered) UnregisterHotKey(window, Id);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public nuint Extra; }

    private delegate void WinEvent(nint hook, uint type, nint window, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, [In] Input[] input, int size);
}
