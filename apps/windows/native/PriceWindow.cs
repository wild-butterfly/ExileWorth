using System.Diagnostics;

namespace Vaaluation.Windows;

internal sealed class PriceWindow : Form
{
    private readonly ComboBox league = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 185 };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(820, 0) };
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

        Text = "Vaaluation — Path of Exile";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 490);
        MinimumSize = new Size(670, 390);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        ShowInTaskbar = true;
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        toolbar.Controls.AddRange([league, paste, search, open]);

        listings.Columns.Add("Asking price", 140);
        listings.Columns.Add("Seller", 160);
        listings.Columns.Add("Item level", 75);
        listings.Columns.Add("Status", 80);

        var results = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6 };
        var properties = new GroupBox { Dock = DockStyle.Fill, Text = "Match item modifiers" };
        properties.Controls.Add(filters);
        results.Panel1.Controls.Add(properties);
        results.Panel2.Controls.Add(listings);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(toolbar);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Hover an item in windowed Path of Exile and press Ctrl + Alt + D. Esc hides this window.",
            Padding = new Padding(0, 5, 0, 5)
        });
        layout.Controls.Add(status);
        layout.Controls.Add(results);
        Controls.Add(layout);
        Shown += async (_, _) =>
        {
            results.SplitterDistance = Math.Max(100, results.Width / 3);
            await RunAsync(LoadLeaguesAsync);
        };

        menu.Items.Add("Open Vaaluation", null, (_, _) => Reveal());
        menu.Items.Add("Quit", null, (_, _) => { quitting = true; Close(); });
        tray = new() { Text = "Vaaluation", Icon = SystemIcons.Application, Visible = true, ContextMenuStrip = menu };
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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        hotkey = new GameHotkey(Handle, active =>
        {
            if (!active && TopMost) TopMost = false;
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
                        TopMost = true;
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

    private void Reveal()
    {
        TopMost = false;
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
