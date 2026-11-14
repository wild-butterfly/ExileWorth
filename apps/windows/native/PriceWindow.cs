using System.Diagnostics;

namespace ExileWorth.Windows;

internal sealed class PriceWindow : Form
{
    private readonly GlassButton league = new() { Text = "League ▾", AutoSize = true };
    private readonly ContextMenuStrip leagueMenu = new();
    private readonly GlassButton currencyFilter = new() { Text = "Currency: All ▾", AutoSize = true };
    private readonly ContextMenuStrip currencyMenu = new();
    private string? listingCurrency;
    private string currencyLabel = "All";
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly DataGridView listings = new() { Dock = DockStyle.Fill };
    private readonly Label itemTitle = new() { Text = "Price Check", AutoSize = true };
    private readonly Label estimate = new() { AutoSize = true };
    private readonly CheckedListBox filters = new ModifierList() { Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly Button paste = new GlassButton() { Text = "Check clipboard", AutoSize = true };
    private readonly Button search = new GlassButton(true) { Text = "Apply filters", AutoSize = true, Enabled = false };
    private readonly Button open = new GlassButton() { Text = "Open trade search", AutoSize = true, Enabled = false };
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly Worker worker = new();
    private readonly GamePriceOverlay overlay;
    private Listing[] currentListings = [];
    private readonly string settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Vaaluation", "league.txt");
    private DesktopPreferences preferences;
    private readonly DesktopPages pages;
    private readonly Panel pageHost = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel navigation = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 8, 12, 0) };
    private GameHotkey? hotkey;
    private string? preferred;
    private Uri? tradeUrl;
    private bool quitting, busy, hasItem;

    public PriceWindow()
    {
        try { if (File.Exists(settingsPath)) preferred = File.ReadAllText(settingsPath).Trim(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }

        preferences = DesktopPreferences.Load();
        Text = "ExileWorth — Path of Exile";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 665);
        MinimumSize = new Size(460, 480);
        BackColor = WindowTheme.Background;
        ForeColor = WindowTheme.Foreground;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        ShowInTaskbar = true;
        league.AccessibleName = "Trade league";
        leagueMenu.BackColor = WindowTheme.Surface;
        leagueMenu.ForeColor = WindowTheme.Foreground;
        league.Click += (_, _) => leagueMenu.Show(league, new Point(0, league.Height));
        currencyFilter.AccessibleName = "Listing currency";
        currencyMenu.BackColor = WindowTheme.Surface;
        currencyMenu.ForeColor = WindowTheme.Foreground;
        foreach (var (label, id) in new (string, string?)[]
        {
            ("All", null), ("Alchemy", "alch"), ("Chaos", "chaos"), ("Divine", "divine"),
        })
        {
            var option = new ToolStripMenuItem(label) { Checked = id is null };
            option.Click += async (_, _) => await SelectCurrencyAsync(id);
            currencyMenu.Items.Add(option);
        }
        currencyFilter.Click += (_, _) => currencyMenu.Show(currencyFilter, new Point(0, currencyFilter.Height));
        listings.AccessibleName = "Trade listings";
        WindowTheme.StyleListings(listings);
        filters.AccessibleName = "Item modifiers";
        WindowTheme.StyleFilters(filters);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 5,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new PictureBox
        {
            Image = Icon.ToBitmap(), Size = new Size(32, 32), SizeMode = PictureBoxSizeMode.Zoom,
            Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0),
        });
        header.Controls.Add(new Label
        {
            Text = "ExileWorth", AutoSize = true, Anchor = AnchorStyles.Left,
            ForeColor = WindowTheme.Foreground, Font = new Font(Font, FontStyle.Bold),
        });
        header.Controls.Add(league);
        layout.Controls.Add(header);

        var hero = new GlassPanel(accent: true) { Dock = DockStyle.Fill, AutoSize = true };
        var summary = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        itemTitle.Font = new Font(Font.FontFamily, 18, FontStyle.Bold);
        itemTitle.AutoSize = false;
        itemTitle.AutoEllipsis = true;
        itemTitle.Height = itemTitle.Font.Height + 4;
        itemTitle.Dock = DockStyle.Fill;
        estimate.Font = new Font(Font.FontFamily, 15, FontStyle.Bold);
        estimate.ForeColor = WindowTheme.Accent;
        estimate.Anchor = AnchorStyles.Right;
        status.ForeColor = WindowTheme.Muted;
        status.Margin = new Padding(3, 4, 3, 0);
        summary.Controls.Add(itemTitle, 0, 0);
        summary.Controls.Add(estimate, 1, 0);
        summary.Controls.Add(status, 0, 1);
        summary.SetColumnSpan(status, 2);
        hero.Controls.Add(summary);
        layout.Controls.Add(hero);
        layout.Controls.Add(ResultPanel("Match item modifiers", filters));
        layout.Controls.Add(ResultPanel("Trade listings", listings));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = true,
            Padding = new Padding(0, 4, 0, 0),
        };
        toolbar.Controls.AddRange([search, open, paste, currencyFilter]);
        layout.Controls.Add(toolbar);
        pages = new DesktopPages(preferences, SavePreferences, () => preferred, SelectLeague);
        var price = new GlassButton(true) { Text = "Price Check", AutoSize = true };
        var currency = new GlassButton { Text = "Currency", AutoSize = true };
        var settings = new GlassButton { Text = "Settings", AutoSize = true };
        price.Click += (_, _) => ShowPage(null);
        currency.Click += (_, _) => ShowPage(pages.Currency);
        settings.Click += (_, _) => ShowPage(pages.Settings);
        navigation.Controls.AddRange([price, currency, settings]);
        overlay = new GamePriceOverlay(id => _ = SelectCurrencyAsync(id));
        Controls.Add(layout);
        Controls.Add(pageHost);
        Controls.Add(navigation);
        ShowPage(null);
        Shown += async (_, _) => await RunAsync(LoadLeaguesAsync);

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
        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            Hide();
            e.SuppressKeyPress = true;
        };
    }

    private void ShowPage(Control? page)
    {
        pageHost.Controls.Clear();
        if (page is not null) pageHost.Controls.Add(page);
        pageHost.Visible = page is not null;
        if (page is not null) pageHost.BringToFront();
        navigation.BringToFront();
    }

    private void SavePreferences(DesktopPreferences updated)
    {
        updated.Save();
        preferences = updated;
        hotkey?.SetShortcut(updated.Shortcut);
        if (!hasItem && !busy) ShowShortcutHint();
    }

    private void ShowShortcutHint() =>
        status.Text = $"Hover an item in Path of Exile and press {preferences.Shortcut}, or check the clipboard.";

    private static Control ResultPanel(string title, Control content)
    {
        var panel = new GlassPanel { Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = title, AutoSize = true, ForeColor = WindowTheme.Muted, Margin = new Padding(0, 0, 0, 9),
        });
        content.Margin = Padding.Empty;
        layout.Controls.Add(content);
        panel.Controls.Add(layout);
        return panel;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowTheme.ApplyTitleBar(Handle);
        hotkey = new GameHotkey(Handle, preferences.Shortcut, active =>
        {
            if (!active) overlay?.Hide();
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
        status.Text = "Copying item from Path of Exile…";
        currentListings = [];
        string? text;
        try { text = await GameHotkey.CopyItemAsync(game, preferences.Shortcut.Key); }
        finally
        {
            if (!IsDisposed && GameHotkey.ForegroundGame() == game)
            {
                overlay.Open(game, preferences.NearCursor);
                UpdateOverlay();
            }
        }
        if (IsDisposed) return;
        if (text is null)
        {
            status.Text = "Item copy cancelled. Release the shortcut keys and try again.";
            return;
        }
        await CheckAsync(text, null);
    });

    private async Task RunAsync(Func<Task> operation)
    {
        if (busy) return;
        busy = true;
        league.Enabled = currencyFilter.Enabled = paste.Enabled = search.Enabled = open.Enabled = false;
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
                league.Enabled = currencyFilter.Enabled = paste.Enabled = true;
                search.Enabled = hasItem;
                open.Enabled = tradeUrl is not null;
                UpdateOverlay();
            }
        }
    }

    private void UpdateOverlay()
    {
        if (overlay.Visible)
            overlay.Present(itemTitle.Text, status.Text, currentListings, listingCurrency, busy || !hasItem);
    }

    private async Task SelectCurrencyAsync(string? id)
    {
        if (busy || listingCurrency == id) return;
        listingCurrency = id;
        currencyLabel = id switch { "alch" => "Alchemy", "chaos" => "Chaos", "divine" => "Divine", _ => "All" };
        currencyFilter.Text = $"Currency: {currencyLabel} ▾";
        foreach (ToolStripMenuItem option in currencyMenu.Items)
            option.Checked = option.Text == currencyLabel;
        if (hasItem) await RunAsync(() => CheckAsync(null, SelectedFilters()));
    }

    private async Task LoadLeaguesAsync()
    {
        status.Text = "Loading trade leagues…";
        var data = await worker.SendAsync<LeagueResult>(new { action = "leagues", preferred });
        if (IsDisposed) return;
        SetLeagues(data.Leagues, data.Selected);
        ShowShortcutHint();
    }

    private async Task CheckAsync(string? text, string[]? enabled)
    {
        tradeUrl = null;
        if (text is not null)
        {
            hasItem = false;
            filters.Items.Clear();
            itemTitle.Text = "Price Check";
        }
        estimate.Text = "";
        listings.Rows.Clear();
        currentListings = [];
        status.Text = "Looking up trade listings…";
        UpdateOverlay();
        var result = await worker.SendAsync<PriceResult>(new { action = "check", preferred, text, enabled, currency = listingCurrency });
        if (IsDisposed) return;
        SetLeagues(result.Leagues, result.Selected);
        hasItem = true;
        filters.Items.Clear();
        foreach (var filter in result.Filters) filters.Items.Add(filter, filter.Enabled);

        itemTitle.Text = result.ItemName ?? "Price Check";
        if (result.Listings.FirstOrDefault() is { } first)
            estimate.Text = $"First ask: {first.Amount:g} {first.Currency}";
        currentListings = result.Listings;
        foreach (var item in currentListings)
            listings.Rows.Add($"{item.Amount:g} {item.Currency}", item.Ilvl?.ToString() ?? "—",
                item.AccountName, item.Presence);
        listings.ClearSelection();
        if (Uri.TryCreate(result.TradeUrl, UriKind.Absolute, out var url) &&
            url.Scheme == Uri.UriSchemeHttps && url.Host == "www.pathofexile.com")
            tradeUrl = url;
        string scope = listingCurrency is null ? result.Selected : $"{result.Selected} · {currencyLabel}";
        status.Text = result.Total == 0
            ? $"No matches in {scope}. Try All currencies or fewer modifiers."
            : $"{scope}: {result.Total:N0} matches. Showing {result.Listings.Length} asking prices.";
    }

    private string[] SelectedFilters() => filters.CheckedItems.Cast<Modifier>().Select(f => f.Key).ToArray();

    private void SetLeagues(League[] available, string? selected)
    {
        pages.SetLeagues(available, selected);
        var current = available.FirstOrDefault(entry => entry.Id == selected);
        league.Text = current is null ? "League ▾" : $"{current.Text} ▾";
        while (leagueMenu.Items.Count > 0) leagueMenu.Items[0].Dispose();
        foreach (var entry in available)
        {
            var option = new ToolStripMenuItem(entry.Text) { Checked = entry.Id == selected };
            option.Click += async (_, _) => await SelectLeague(entry);
            leagueMenu.Items.Add(option);
        }
    }

    private async Task SelectLeague(League entry)
    {
        if (busy) return;
        preferred = entry.Id;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, entry.Id);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        SetLeagues(pages.Available, entry.Id);
        if (hasItem) await RunAsync(() => CheckAsync(null, SelectedFilters()));
    }

    private void Reveal()
    {
        overlay.Hide();
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
            leagueMenu.Dispose();
            currencyMenu.Dispose();
            pages?.Dispose();
            overlay?.Dispose();
            worker.Dispose();
        }
        base.Dispose(disposing);
    }
}
