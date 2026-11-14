using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var game = new GameFixture { Text = "Vaaluation game fixture", Width = 400, Height = 240, KeyPreview = true };
        const string item = "Item Class: Stackable Currency\r\nRarity: Currency\r\nDivine Orb";
        int copies = 0;
        game.ItemCopied += () =>
        {
            Clipboard.SetText(item);
            copies++;
            Console.WriteLine($"Fixture received copy #{copies}.");
        };

        game.Shown += async (_, _) =>
        {
            Process? app = null;
            try
            {
                app = Process.Start(args[0]) ?? throw new Exception("App could not start.");
                await Until(() => FindAppWindow(app.Id) != 0, "app window");
                nint overlay = FindAppWindow(app.Id);
                await Task.Delay(1600); // Let the packaged worker load the fixture league.
                Foreground(overlay);
                await Until(() => GetForegroundWindow() == overlay, "app focus");
                SendChord(Keys.D);
                await Task.Delay(750);
                Assert(copies == 0 && !IsTopmost(overlay), "Shortcut ran outside the game.");

                PostMessageW(overlay, 0x0010, 0, 0); // Close-to-tray must keep the shortcut registered.
                await Until(() => !IsWindowVisible(overlay), "hide to tray");
                Foreground(game.Handle);
                await Until(() => GetForegroundWindow() == game.Handle, "game focus");
                await Task.Delay(750);
                SendChord(Keys.D);
                await Until(() => copies >= 1, "game copy chord");
                await Until(() => IsTopmost(overlay) && IsWindowVisible(overlay), "visible topmost overlay");
                Assert(copies == 1, "Expected exactly one copy.");
                Assert(GetForegroundWindow() == game.Handle, "Overlay stole game focus.");
                Assert(Clipboard.GetText() == item, "Clipboard did not contain the captured item.");

                Foreground(overlay);
                await Until(() => GetForegroundWindow() == overlay && !IsTopmost(overlay), "overlay no longer topmost after switch");
                SendChord(Keys.D);
                await Task.Delay(750);
                Assert(copies == 1, "Shortcut fired outside the game.");
                Console.WriteLine("PASS: game-only shortcut, one copy, visible topmost overlay, game focus retained, topmost cleared off-game.");
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"copies={copies}; foreground={GetForegroundWindow()}; game={game.Handle}; app={app?.Id}; exited={app?.HasExited}");
                Console.Error.WriteLine(error);
                Environment.ExitCode = 1;
            }
            finally
            {
                if (app is not null)
                {
                    if (!app.HasExited) app.Kill(entireProcessTree: true);
                    app.Dispose();
                }
                game.Close();
            }
        };
        Application.Run(game);
    }

    private sealed class GameFixture : Form
    {
        public event Action? ItemCopied;
        protected override void WndProc(ref Message m)
        {
            if ((m.Msg == 0x0100 || m.Msg == 0x0104) &&
                m.WParam == (nint)Keys.C && GetKeyState((int)Keys.ControlKey) < 0 &&
                GetKeyState((int)Keys.Menu) < 0)
            {
                ItemCopied?.Invoke();
                return;
            }
            base.WndProc(ref m);
        }
        [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task Until(Func<bool> check, string description)
    {
        for (int i = 0; i < 120; i++)
        {
            if (check()) return;
            await Task.Delay(50);
        }
        throw new InvalidOperationException($"Timed out waiting for {description}.");
    }

    private static nint FindAppWindow(int pid)
    {
        nint found = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != pid || !IsWindowVisible(window)) return true;
            found = window;
            return false;
        }, 0);
        return found;
    }

    private static void Foreground(nint window)
    {
        // Alt enables a foreground transition in the isolated test desktop.
        keybd_event((byte)Keys.Menu, 0, 0, 0);
        keybd_event((byte)Keys.Menu, 0, 2, 0);
        SetForegroundWindow(window);
    }

    private static void SendChord(Keys key)
    {
        foreach (Keys k in new[] { Keys.ControlKey, Keys.Menu, key }) keybd_event((byte)k, 0, 0, 0);
        foreach (Keys k in new[] { key, Keys.Menu, Keys.ControlKey }) keybd_event((byte)k, 0, 2, 0);
    }

    private static bool IsTopmost(nint window) => (GetWindowLongW(window, -20) & 8) != 0;
    private delegate bool WindowCallback(nint window, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint window, int index);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
}
