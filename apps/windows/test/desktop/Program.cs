using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 3 && args[0] == "--capture")
        {
            Environment.ExitCode = CaptureDesktop(args[1], args[2]).GetAwaiter().GetResult();
            return;
        }
        using var game = new GameFixture { Text = "ExileWorth game fixture", WindowState = FormWindowState.Maximized, BackColor = Color.FromArgb(38, 34, 31), KeyPreview = true };
        game.Controls.Add(new Label
        {
            Text = "Windows overlay test fixture", AutoSize = true,
            Location = new Point(24, 24), ForeColor = Color.White,
        });
        const string item = "Item Class: Stackable Currency\r\nRarity: Currency\r\nDivine Orb";
        int copies = 0;
        int gameClicks = 0;
        game.MouseDown += (_, _) => gameClicks++;
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
                string logFolder = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(logFolder);
                File.WriteAllText(Path.Combine(logFolder, "Client.txt"),
                    "2026/11/15 21:00:00 123456 cffb0716 [INFO Client 1234] @From FixtureBuyer: Hi, I would like to buy your Goldrim listed for 1 chaos in Covens");
                string verifiedReplies = Path.Combine(logFolder, "replies-verified");
                File.Delete(verifiedReplies);
                var start = new ProcessStartInfo(args[0]);
                start.Environment["EXILEWORTH_TEST_REPLIES"] = verifiedReplies;
                app = Process.Start(start) ?? throw new Exception("App could not start.");
                await Until(() => FindAppWindow(app.Id) != 0, "app window");
                nint desktop = FindAppWindow(app.Id);
                nint overlay = 0;
                await Task.Delay(1600); // Let the packaged worker load the fixture league.
                Foreground(desktop);
                await Until(() => GetForegroundWindow() == desktop, "app focus");
                SendChord(Keys.D);
                await Task.Delay(750);
                Assert(copies == 0 && !IsTopmost(desktop), "Shortcut ran outside the game.");

                InvokeButton(desktop, "Settings");
                InvokeButton(desktop, "+ Add reply");
                await Until(() => FindButton(desktop, "Remove reply 4") is not null, "added reply in Settings");
                InvokeButton(desktop, "Remove reply 1");
                await Until(() => FindButton(desktop, "Remove reply 4") is null, "removed reply in Settings");
                InvokeButton(desktop, "Price Check");

                PostMessageW(desktop, 0x0112, 0xF060, 0); // Close-to-tray must keep the shortcut registered.
                await Until(() => !IsWindowVisible(desktop), "hide to tray");
                Foreground(game.Handle);
                await Until(() => GetForegroundWindow() == game.Handle, "game focus");
                await Task.Delay(750);
                SendChord(Keys.D);
                await Until(() => (overlay = FindAppWindow(app.Id, desktop)) != 0 && IsTopmost(overlay),
                    "visible compact overlay");
                await Until(() => copies >= 1, "game copy chord");
                Assert(!IsWindowVisible(desktop), "Game shortcut showed the desktop window.");
                Assert(GetWindowRect(overlay, out var bounds) && bounds.Bottom - bounds.Top <= 440,
                    "Game overlay was not compact.");
                Assert(copies == 1, "Expected exactly one copy.");
                Assert(GetForegroundWindow() == game.Handle, "Overlay stole game focus.");
                Assert(Clipboard.GetText() == item, "Clipboard did not contain the captured item.");

                if (args.Length > 1)
                {
                    await Task.Delay(350);
                    CaptureOverlay(overlay, args[1]);
                }

                // A click inside the panel (its status line) must not close it or take focus.
                Click(new Point(bounds.Left + 60, bounds.Bottom - 12));
                await Task.Delay(500);
                Assert(IsWindowVisible(overlay), "A click inside the overlay closed it.");
                Assert(GetForegroundWindow() == game.Handle, "A click inside the overlay took game focus.");

                int clicksBeforeDismissal = gameClicks;
                Click(GameBackground(game.Handle));
                await Until(() => !IsWindowVisible(overlay), "overlay dismissed by game click");
                await Until(() => gameClicks > clicksBeforeDismissal, "outside click delivered to game");
                Assert(GetForegroundWindow() == game.Handle, "Outside click changed game focus.");

                // The fixture worker answers this second lookup late. Dismiss the panel
                // while it is pending; the answer must not bring the panel back.
                await Task.Delay(400);
                SendChord(Keys.D);
                await Until(() => copies >= 2, "second game copy chord");
                await Until(() => IsWindowVisible(overlay), "overlay for the pending lookup");
                Click(GameBackground(game.Handle));
                await Until(() => !IsWindowVisible(overlay), "overlay dismissed during a pending lookup");
                await Task.Delay(3500); // Longer than the fixture's delayed answer.
                Assert(!IsWindowVisible(overlay), "A pending lookup reopened the dismissed overlay.");
                Assert(GetForegroundWindow() == game.Handle, "The pending lookup took game focus.");

                SendChord(Keys.D);
                await Until(() => copies >= 3 && IsWindowVisible(overlay), "price overlay before opening Trades");
                InvokeButton(overlay, "Trades");
                await Until(() => File.Exists(verifiedReplies), "edited replies reaching the overlay trade worker");
                Console.WriteLine("PASS: Settings add and remove propagated to the Trades overlay.");
                Click(GameBackground(game.Handle));
                await Until(() => !IsWindowVisible(overlay), "dismiss the Trades overlay");

                ShowWindow(desktop, 4);
                Foreground(desktop);
                await Until(() => GetForegroundWindow() == desktop && !IsWindowVisible(overlay),
                    "overlay hidden after switching away");
                SendChord(Keys.D);
                await Task.Delay(750);
                Assert(copies == 3, "Shortcut fired outside the game.");
                if (args.Length > 2)
                {
                    // Hover the second sidebar row so the capture shows hovered and selected states.
                    var row = new WindowPoint { X = 107, Y = 130 };
                    ClientToScreen(desktop, ref row);
                    Cursor.Position = new Point(row.X, row.Y);
                    await Task.Delay(600);
                    CaptureOverlay(desktop, args[2]);
                }
                Console.WriteLine("PASS: compact overlay kept game focus and hid after switching away.");
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"copies={copies}; foreground={GetForegroundWindow()}; game={game.Handle}; app={app?.Id}; exited={app?.HasExited}; exitCode={(app?.HasExited == true ? app.ExitCode : null)}");
                Console.Error.WriteLine(error);
                if (args.Length > 1 && app is { HasExited: false })
                {
                    nint shown = FindAppWindow(app.Id);
                    if (shown != 0)
                        CaptureOverlay(shown, Path.Combine(Path.GetDirectoryName(args[1])!, "windows-overlay-failure.png"));
                }
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

    /// <summary>Starts a packaged build, hovers the second sidebar row, and captures the window.</summary>
    private static async Task<int> CaptureDesktop(string exe, string path)
    {
        using var app = Process.Start(exe) ?? throw new Exception("App could not start.");
        try
        {
            await Until(() => FindAppWindow(app.Id) != 0, "app window");
            nint desktop = FindAppWindow(app.Id);
            await Task.Delay(2500); // Let the window lay out and the fixture league load.
            Foreground(desktop);
            var row = new WindowPoint { X = 107, Y = 130 };
            ClientToScreen(desktop, ref row);
            Cursor.Position = new Point(row.X, row.Y);
            await Task.Delay(700);
            CaptureOverlay(desktop, path);
            Console.WriteLine($"Captured {exe} to {path}.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            if (!app.HasExited) app.Kill(entireProcessTree: true);
        }
    }

    private static void CaptureOverlay(nint overlay, string path)
    {
        if (!GetWindowRect(overlay, out var window))
            throw new InvalidOperationException("Could not locate the overlay for capture.");
        var overlayBounds = Rectangle.FromLTRB(window.Left, window.Top, window.Right, window.Bottom);
        var bounds = Rectangle.Intersect(Screen.FromHandle(overlay).Bounds,
            Rectangle.Inflate(overlayBounds, 28, 28));
        using var image = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(image);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out WindowRect bounds);

    private sealed class GameFixture : Form
    {
        public event Action? ItemCopied;
        private const int CopyReceived = 0x8001;
        private KeyboardHook? keyboardHook;
        private nint hook;
        private bool leftControl, rightControl, shift, alt;

        protected override void OnShown(EventArgs e)
        {
            // Observe keyboard input before WinForms can consume the chord.
            // The fixture only supplies item text for plain Ctrl+C sent to the game.
            keyboardHook = ObserveKeyboard;
            hook = SetWindowsHookExW(13, keyboardHook, GetModuleHandleW(null), 0);
            if (hook == 0) throw new System.ComponentModel.Win32Exception();
            base.OnShown(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (hook != 0) UnhookWindowsHookEx(hook);
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == CopyReceived)
            {
                ItemCopied?.Invoke();
                return;
            }
            base.WndProc(ref m);
        }

        private nint ObserveKeyboard(int code, nint message, nint data)
        {
            if (code == 0)
            {
                Keys key = (Keys)Marshal.ReadInt32(data);
                bool down = (int)message is 0x0100 or 0x0104;
                switch (key)
                {
                    case Keys.LControlKey: leftControl = down; break;
                    case Keys.RControlKey: rightControl = down; break;
                    case Keys.LShiftKey or Keys.RShiftKey: shift = down; break;
                    case Keys.LMenu or Keys.RMenu: alt = down; break;
                    case Keys.C when down && GetForegroundWindow() == Handle:
                        Console.WriteLine($"Fixture observed C: Ctrl={leftControl || rightControl}, Shift={shift}, Alt={alt}.");
                        if ((leftControl || rightControl) && !shift && !alt)
                            PostMessageW(Handle, CopyReceived, 0, 0);
                        break;
                }
            }
            return CallNextHookEx(hook, code, message, data);
        }
    }

    private delegate nint KeyboardHook(int code, nint message, nint data);
    [DllImport("kernel32.dll")] private static extern nint GetModuleHandleW(string? module);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(int type, KeyboardHook callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    private static AutomationElement? FindButton(nint window, string name) =>
        AutomationElement.FromHandle(window).FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.NameProperty, name)));

    private static void InvokeButton(nint window, string name)
    {
        var button = FindButton(window, name) ?? throw new InvalidOperationException($"Missing button: {name}");
        ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
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

    private static nint FindAppWindow(int pid, nint exclude = 0)
    {
        nint found = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != pid || window == exclude || !IsWindowVisible(window)) return true;
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
        foreach (Keys k in new[] { Keys.LControlKey, Keys.LShiftKey, key }) SendKey(k, 0);
        foreach (Keys k in new[] { key, Keys.LShiftKey, Keys.LControlKey }) SendKey(k, 2);
    }

    private static void SendKey(Keys key, uint flags) =>
        keybd_event((byte)key, (byte)MapVirtualKeyW((uint)key, 0), flags, 0);

    [DllImport("user32.dll")] private static extern uint MapVirtualKeyW(uint code, uint mapType);

    private static bool IsTopmost(nint window) => (GetWindowLongW(window, -20) & 8) != 0;
    private delegate bool WindowCallback(nint window, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint window, int index);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);

    private static Point GameBackground(nint game)
    {
        Rectangle area = Screen.FromHandle(game).WorkingArea;
        // Pick an exposed spot in the fixture's client area, away from its label,
        // title bar, and the overlay (which can move between price checks).
        for (int y = area.Bottom - 48; y > area.Top + 120; y -= 48)
        for (int x = area.Left + 32; x < area.Right - 32; x += 48)
        {
            if (WindowFromPoint(new WindowPoint { X = x, Y = y }) == game)
                return new Point(x, y);
        }
        throw new InvalidOperationException("No exposed game background to click.");
    }

    [DllImport("user32.dll")] private static extern nint WindowFromPoint(WindowPoint point);

    private static void Click(Point point)
    {
        Cursor.Position = point;
        mouse_event(0x0002, 0, 0, 0, 0);
        mouse_event(0x0004, 0, 0, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref WindowPoint point);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
}
