using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ExileWorth.Windows;

internal sealed class PriceWindow : Form
{
    private readonly ComboBox league = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 185 };
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly ListView listings = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly CheckedListBox filters = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly Button paste = new() { Text = "Check clipboard", AutoSize = true };
    private readonly Button search = new() { Text = "Apply filters", AutoSize = true, Enabled = false };
    private readonly Button open = new() { Text = "Open trade search", AutoSize = true, Enabled = false };
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly Worker worker = new();
    private readonly string settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Vaaluation", "league.txt");
    private GameHotkey? hotkey;
    private string? preferred;
    private Uri? tradeUrl;
    private bool quitting, busy, loadingLeagues, hasItem, passive;

    protected override bool ShowWithoutActivation => passive;

    public PriceWindow()
    {
        try { if (File.Exists(settingsPath)) preferred = File.ReadAllText(settingsPath).Trim(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }

        Text = "ExileWorth — Path of Exile";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(820, 510);
        MinimumSize = new Size(700, 420);
        BackColor = WindowTheme.Background;
        ForeColor = WindowTheme.Foreground;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        ShowInTaskbar = true;
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
            Padding = new Padding(0, 8, 0, 6),
        };
        league.AccessibleName = "Trade league";
        WindowTheme.StyleLeague(league);
        WindowTheme.StyleButton(paste, primary: true);
        WindowTheme.StyleButton(search);
        WindowTheme.StyleButton(open);
        toolbar.Controls.AddRange([league, paste, search, open]);

        listings.Columns.Add("Asking price", 130);
        listings.Columns.Add("Seller", 140);
        listings.Columns.Add("Item level", 70);
        listings.Columns.Add("Status", 85);
        WindowTheme.StyleListings(listings);
        listings.AccessibleName = "Trade listings";
        filters.BackColor = WindowTheme.Surface;
        filters.ForeColor = WindowTheme.Foreground;
        filters.BorderStyle = BorderStyle.None;
        filters.AccessibleName = "Item modifiers";

        var results = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 12 };
        results.Panel1.Controls.Add(ResultPanel("Match item modifiers", filters));
        results.Panel2.Controls.Add(ResultPanel("Trade listings", listings));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 4; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            AutoSize = true, Text = "Price Check", Font = new Font(Font.FontFamily, 21, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6),
        });
        layout.Controls.Add(new Label
        {
            AutoSize = true, ForeColor = WindowTheme.Muted, Margin = Padding.Empty,
            Text = "Hover an item in Path of Exile and press Ctrl + Alt + D. Press Esc to hide.",
        });
        layout.Controls.Add(toolbar);
        status.BackColor = WindowTheme.Surface;
        status.ForeColor = WindowTheme.Muted;
        status.Padding = new Padding(10, 8, 10, 8);
        status.Margin = new Padding(0, 0, 0, 12);
        layout.Controls.Add(status);
        layout.Controls.Add(results);
        Controls.Add(layout);
        Shown += async (_, _) =>
        {
            results.SplitterDistance = Math.Max(100, results.Width / 3);
            await RunAsync(LoadLeaguesAsync);
        };

        menu.Items.Add("Open ExileWorth", null, (_, _) => Reveal());
        menu.Items.Add("Quit", null, (_, _) => { quitting = true; Close(); });
        tray = new() { Text = "ExileWorth", Icon = Icon, Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Reveal();

        paste.Click += async (_, _) => await RunAsync(async () =>
        {
            if (!Clipboard.ContainsText()) throw new InvalidOperationException("Copy item text from Path of Exile first.");
            await CheckAsync(Clipboard.GetText(), null);
        });
        search.Click += async (_, _) => await RunAsync(() => CheckAsync(null, SelectedFilters()));
        open.Click += (_, _) =>
        {
            if (tradeUrl is not null) Process.Start(new ProcessStartInfo(tradeUrl.AbsoluteUri) { UseShellExecute = true });
        };
        league.SelectedIndexChanged += async (_, _) =>
        {
            if (loadingLeagues || league.SelectedItem is not League chosen) return;
            preferred = chosen.Id;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                File.WriteAllText(settingsPath, chosen.Id);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (hasItem) await RunAsync(() => CheckAsync(null, SelectedFilters()));
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            Hide();
            e.SuppressKeyPress = true;
        };
    }

    private static Control ResultPanel(string title, Control content)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = WindowTheme.Surface,
            Padding = new Padding(10), ColumnCount = 1, RowCount = 2,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = title, AutoSize = true, Margin = new Padding(0, 0, 0, 9),
        });
        content.Margin = Padding.Empty;
        panel.Controls.Add(content);
        return panel;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowTheme.ApplyTitleBar(Handle);
        hotkey = new GameHotkey(Handle, active =>
        {
            if (!active) ClearOverlayZOrder();
        });
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        hotkey?.Dispose();
        hotkey = null;
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == GameHotkey.Message && m.WParam == GameHotkey.Id)
        {
            nint game = GameHotkey.ForegroundGame();
            if (game != 0 && !busy) _ = CaptureAsync(game);
        }
        base.WndProc(ref m);
    }

    private Task CaptureAsync(nint game) => RunAsync(async () =>
    {
        string? text = await GameHotkey.CopyItemAsync(game);
        if (text is null) return;
        await CheckAsync(text, null);
    }, game);

    private async Task RunAsync(Func<Task> operation, nint game = 0)
    {
        if (busy) return;
        busy = true;
        league.Enabled = paste.Enabled = search.Enabled = open.Enabled = false;
        try
        {
            await operation();
        }
        catch (Exception error)
        {
            if (IsDisposed) return;
            status.Text = error.Message;
        }
        finally
        {
            busy = false;
            if (!IsDisposed)
            {
                league.Enabled = paste.Enabled = true;
                search.Enabled = hasItem;
                open.Enabled = tradeUrl is not null;
                if (game != 0 && GameHotkey.ForegroundGame() == game)
                {
                    passive = true;
                    try
                    {
                        if (!Visible)
                        {
                            Point cursor = Cursor.Position;
                            Rectangle bounds = Screen.FromPoint(cursor).WorkingArea;
                            StartPosition = FormStartPosition.Manual;
                            Location = new Point(
                                Math.Clamp(cursor.X + 18, bounds.Left, Math.Max(bounds.Left, bounds.Right - Width)),
                                Math.Clamp(cursor.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - Height)));
                            Show();
                        }
                        SetWindowPos(Handle, -1, 0, 0, 0, 0, 0x0013); // TOPMOST, NOACTIVATE, NOMOVE, NOSIZE
                    }
                    finally { passive = false; }
                }
            }
        }
    }

    private async Task LoadLeaguesAsync()
    {
        status.Text = "Loading trade leagues…";
        var data = await worker.SendAsync<LeagueResult>(new { action = "leagues", preferred });
        if (IsDisposed) return;
        SetLeagues(data.Leagues, data.Selected);
        status.Text = "Copy an English Path of Exile item to check its price.";
    }

    private async Task CheckAsync(string? text, string[]? enabled)
    {
        tradeUrl = null;
        if (text is not null)
        {
            hasItem = false;
            filters.Items.Clear();
        }
        listings.Items.Clear();
        status.Text = "Looking up trade listings…";
        var result = await worker.SendAsync<PriceResult>(new { action = "check", preferred, text, enabled });
        if (IsDisposed) return;
        SetLeagues(result.Leagues, result.Selected);
        hasItem = true;
        filters.Items.Clear();
        foreach (var filter in result.Filters) filters.Items.Add(filter, filter.Enabled);

        listings.BeginUpdate();
        try
        {
            foreach (var item in result.Listings)
                listings.Items.Add(new ListViewItem(
                    [$"{item.Amount:g} {item.Currency}", item.AccountName, item.Ilvl?.ToString() ?? "—", item.Presence]));
        }
        finally { listings.EndUpdate(); }
        if (Uri.TryCreate(result.TradeUrl, UriKind.Absolute, out var url) &&
            url.Scheme == Uri.UriSchemeHttps && url.Host == "www.pathofexile.com")
            tradeUrl = url;
        status.Text = result.Total == 0
            ? $"No matches in {result.Selected}. Try fewer filters."
            : $"{result.Selected}: {result.Total:N0} matches. Showing {result.Listings.Length} asking prices.";
    }

    private string[] SelectedFilters() => filters.CheckedItems.Cast<Modifier>().Select(f => f.Key).ToArray();

    private void SetLeagues(League[] available, string? selected)
    {
        loadingLeagues = true;
        try
        {
            league.Items.Clear();
            league.Items.AddRange(available);
            league.SelectedIndex = Array.FindIndex(available, entry => entry.Id == selected);
        }
        finally { loadingLeagues = false; }
    }

    private void ClearOverlayZOrder() =>
        SetWindowPos(Handle, -2, 0, 0, 0, 0, 0x0013); // NOTOPMOST, NOACTIVATE, NOMOVE, NOSIZE

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    private void Reveal()
    {
        ClearOverlayZOrder();
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!quitting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tray?.Dispose();
            menu.Dispose();
            worker.Dispose();
        }
        base.Dispose(disposing);
    }
}
