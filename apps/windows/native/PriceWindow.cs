using System.Diagnostics;

namespace ExileWorth.Windows;

internal sealed class PriceWindow : Form
{
    private readonly FlatButton league = new(ButtonKind.Secondary, "League ▾") { AccessibleName = "Trade league" };
    private readonly ContextMenuStrip leagueMenu = WindowTheme.Menu();
    private readonly FlatButton currencyFilter = new(ButtonKind.Chip, "All  ▾") { AccessibleName = "Listing currency" };
    private readonly ContextMenuStrip currencyMenu = WindowTheme.Menu();
    private Modifier[] currentFilters = [];
    private string? listingCurrency;
    private readonly PriceHeader hero = new(compact: false) { Dock = DockStyle.Fill, BackColor = WindowTheme.Card };
    private readonly ListingView listings = new(compact: false) { Dock = DockStyle.Fill };
    private readonly ModifierView filters = new() { Dock = DockStyle.Fill };
    private readonly FlatButton paste = new(ButtonKind.Secondary, "Check clipboard");
    private readonly FlatButton search = new(ButtonKind.Primary, "Search") { Enabled = false };
    private readonly FlatButton open = new(ButtonKind.Ghost, "Open trade site ↗") { Enabled = false };
    private readonly Label listingHint = Ui.Label("", WindowTheme.Sans(8.5f), WindowTheme.Dim);
    private readonly Label pricesHeading = Ui.Eyebrow("Asking prices");
    private readonly Label modifiersHeading = Ui.Eyebrow("Match modifiers");
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = WindowTheme.Menu();
    private readonly Worker worker = new();
    private readonly GamePriceOverlay overlay;
    private Listing[] currentListings = [];
    private readonly string settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Vaaluation", "league.txt");
    private DesktopPreferences preferences;
    private readonly DesktopPages pages;
    private readonly Panel pageHost = new() { Dock = DockStyle.Fill, BackColor = WindowTheme.Background };
    private readonly Control pricePage;
    private readonly List<(FlatButton Button, Control Page)> navigation = [];
    private readonly Label statusTitle = Ui.Label("Waiting for the game", WindowTheme.Sans(8.5f, FontStyle.Bold), WindowTheme.Foreground);
    private readonly Label statusMeta = Ui.Label("", WindowTheme.Mono(7.5f), WindowTheme.Muted);
    private readonly Panel statusDot = new() { Size = new Size(7, 7), Margin = new Padding(0, 6, 7, 0) };
    private readonly System.Windows.Forms.Timer gameWatch = new() { Interval = 3000 };
    private readonly System.Windows.Forms.Timer cooldownTimer = new() { Interval = 1000 };
    private long cooldownUntil;
    private string? copiedItem;
    private int CooldownSeconds => (int)Math.Max(0, (cooldownUntil - Environment.TickCount64 + 999) / 1000);
    private GameHotkey? hotkey;
    private string? preferred;
    private string leagueName = "";
    private string statusText = "";
    private string? itemName;
    private string meta = "";
    private TypicalPrice? typical;
    private ExchangeQuote? exchange;
    private int total;
    private Uri? tradeUrl;
    private bool quitting, busy, hasItem;
    // Started from the Windows Run key: live in the tray until opened.
    private bool startHidden = Environment.GetCommandLineArgs().Skip(1)
        .Contains("--tray", StringComparer.OrdinalIgnoreCase);

    public PriceWindow()
    {
        try { if (File.Exists(settingsPath)) preferred = File.ReadAllText(settingsPath).Trim(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }

        preferences = DesktopPreferences.Load();
        WindowTheme.ApplyTheme(preferences.Theme, notify: false);
        Text = "ExileWorth";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        int S(int value) => value * DeviceDpi / 96;
        ClientSize = new Size(S(980), S(680));
        MinimumSize = new Size(S(780), S(540));
        BackColor = WindowTheme.Background;
        ForeColor = WindowTheme.Foreground;
        Font = WindowTheme.Sans(9.5f);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        KeyPreview = true;
        ShowInTaskbar = true;

        league.Click += (_, _) => leagueMenu.Show(league, new Point(0, league.Height + 4));
        listings.Placeholder = "Listings appear here after a price check.";
        filters.Placeholder = "Modifiers you can match appear here.";
        filters.Changed += () => search.Enabled = hasItem && !busy && CooldownSeconds == 0 && filters.HasValidMinimums;
        listings.RowClicked += (index, _) => CopyWhisper(listings.Listings[index]);

        pricePage = BuildPricePage();
        pages = new DesktopPages(preferences, SavePreferences, () => preferred, SelectLeague, () => preferences);
        overlay = new GamePriceOverlay((id, selected, minimums) => _ = SelectCurrencyAsync(id, selected, minimums),
            (selected, minimums) => _ = RunAsync(() => CheckAsync(null, currentFilters.Length == 0 ? null : selected, minimums)), () => preferences);

        // Docking runs last-added first: the rail takes the left edge, the
        // banner the top of what remains, and the page fills the rest.
        Startup.Repair();
        Controls.Add(pageHost);
        Controls.Add(BuildStartupBanner());
        Controls.Add(BuildRail());
        ShowPage(pricePage);

        menu.Items.Add("Open ExileWorth", null, (_, _) => Reveal());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => { quitting = true; Close(); });
        tray = new() { Text = "ExileWorth", Icon = Icon, Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Reveal();

        paste.Click += async (_, _) => await RunAsync(async () =>
        {
            if (!Clipboard.ContainsText()) throw new InvalidOperationException("Copy item text from Path of Exile first (Ctrl + C).");
            await CheckAsync(Clipboard.GetText(), null);
        });
        search.Click += async (_, _) => await RunAsync(() => CheckAsync(null, filters.ItemCount == 0 ? null : filters.Selected, filters.Minimums));
        open.Click += (_, _) =>
        {
            if (tradeUrl is not null) Process.Start(new ProcessStartInfo(tradeUrl.AbsoluteUri) { UseShellExecute = true });
        };
        KeyDown += (_, e) =>
        {
            if (filters.Editing) return;
            if (e.KeyCode == Keys.Escape) { Hide(); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode is >= Keys.D1 and <= Keys.D9 && (int)(e.KeyCode - Keys.D1) < navigation.Count)
            {
                ShowPage(navigation[e.KeyCode - Keys.D1].Page);
                e.SuppressKeyPress = true;
            }
        };
        gameWatch.Tick += (_, _) => { if (Visible) UpdateGameStatus(); };
        gameWatch.Start();
        cooldownTimer.Tick += (_, _) =>
        {
            int seconds = CooldownSeconds;
            statusText = seconds > 0 ? $"Trade API cooldown · {seconds}s remaining. Requests are paused."
                : "Cooldown finished. Press Search or use the price-check shortcut again.";
            if (seconds == 0) cooldownTimer.Stop();
            SetControlsEnabled();
            Render();
        };
        CurrencyIcons.Changed += RefreshCurrencyFilter;
        WindowTheme.ThemeChanged += OnThemeChanged;
        UpdateGameStatus();
        Render();
    }

    private Control BuildRail()
    {
        int S(int value) => value * DeviceDpi / 96;
        var rail = new Panel { Dock = DockStyle.Left, Width = S(214), BackColor = WindowTheme.Rail };
        rail.Paint += (_, e) =>
        {
            using var line = new Pen(WindowTheme.Hairline);
            e.Graphics.DrawLine(line, rail.Width - 1, 0, rail.Width - 1, rail.Height);
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(S(10), S(22), S(10), S(12)),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var brand = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(S(6), 0, 0, S(18)) };
        var mark = new BrandMark(S(30)) { Margin = new Padding(0, 0, S(10), 0) };
        brand.Controls.Add(mark, 0, 0);
        brand.SetRowSpan(mark, 2);
        var name = Ui.Label("ExileWorth", WindowTheme.Sans(11f, FontStyle.Bold), WindowTheme.Foreground);
        name.Margin = new Padding(0, S(1), 0, 0);
        // The version makes a stale copy left running in the tray easy to spot.
        var sub = Ui.Label($"PRICE CHECK · v{Program.Version}", WindowTheme.Mono(6.5f), WindowTheme.Dim);
        sub.Margin = Padding.Empty;
        brand.Controls.Add(name, 1, 0);
        brand.Controls.Add(sub, 1, 1);
        layout.Controls.Add(brand, 0, 0);

        var nav = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var (label, glyph, page) in new (string, string, Control)[]
        {
            ("Price Check", "search", pricePage), ("Trades", "trades", pages.Trades), ("Currency", "currency", pages.Currency),
            ("Settings", "settings", pages.Settings),
        })
        {
            var button = new FlatButton(ButtonKind.Nav, label)
            {
                Glyph = glyph, Dock = DockStyle.Top, Height = S(38),
                Hint = $"Ctrl+{navigation.Count + 1}",
            };
            button.Click += (_, _) => ShowPage(page);
            navigation.Add((button, page));
            nav.Add(button);
        }
        layout.Controls.Add(nav, 0, 1);

        var status = new Card { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(S(12)), Margin = Padding.Empty };
        var statusStack = Ui.Stack();
        statusStack.Dock = DockStyle.Top;
        statusDot.Size = new Size(S(7), S(7));
        statusDot.Paint += (_, e) =>
        {
            e.Graphics.Clear(status.BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(statusDot.ForeColor);
            e.Graphics.FillEllipse(brush, 0, 0, statusDot.Width - 1, statusDot.Height - 1);
        };
        statusTitle.Margin = Padding.Empty;
        statusStack.Add(Ui.Row(statusDot, statusTitle));
        statusMeta.Margin = new Padding(0, S(3), 0, 0);
        statusStack.Add(statusMeta);
        status.Controls.Add(statusStack);
        layout.Controls.Add(status, 0, 2);
        rail.Controls.Add(layout);
        return rail;
    }

    private Control BuildPricePage()
    {
        int S(int value) => value * DeviceDpi / 96;
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = WindowTheme.Background,
            Padding = new Padding(S(24), S(20), S(24), S(20)),
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, S(112)));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Title row: heading and the league picker.
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, S(14)) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titles = Ui.Stack();
        titles.Add(Ui.Label("Price Check", WindowTheme.Display(18f), WindowTheme.Foreground)).Margin = Padding.Empty;
        titles.Add(Ui.Label($"Hover an item in Path of Exile and press {preferences.Shortcut}.", WindowTheme.Sans(9f), WindowTheme.Muted))
            .Name = "shortcutHint";
        top.Controls.Add(titles, 0, 0);
        league.Anchor = AnchorStyles.Right;
        league.Margin = Padding.Empty;
        top.Controls.Add(league, 1, 0);
        page.Controls.Add(top, 0, 0);
        page.SetColumnSpan(top, 2);

        var heroCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(S(18), S(14), S(18), S(12)) };
        heroCard.Controls.Add(hero);
        page.Controls.Add(heroCard, 0, 1);
        page.SetColumnSpan(heroCard, 2);

        // Modifiers card.
        var modCard = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, S(12), 0), Padding = new Padding(S(14)) };
        var mods = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        mods.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mods.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mods.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mods.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mods.Controls.Add(modifiersHeading, 0, 0);
        filters.Margin = new Padding(0, 0, 0, S(10));
        mods.Controls.Add(filters, 0, 1);
        mods.Controls.Add(Ui.Row(search, paste), 0, 2);
        modCard.Controls.Add(mods);
        page.Controls.Add(modCard, 0, 2);

        // Listings card with the currency filter as small icon chips.
        var listCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(S(14)) };
        var list = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        list.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var listTop = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        listTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        listTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        listTop.Controls.Add(pricesHeading, 0, 0);
        currencyFilter.Margin = new Padding(0, 0, 0, S(6));
        currencyMenu.ShowImageMargin = true;
        foreach (var (label, id, _) in GamePriceOverlay.CurrencyFilters)
        {
            var option = new ToolStripMenuItem(label) { Tag = id, Checked = id is null };
            option.Click += async (_, _) => await SelectCurrencyAsync(id);
            currencyMenu.Items.Add(option);
        }
        currencyFilter.Click += (_, _) => currencyMenu.Show(currencyFilter, new Point(0, currencyFilter.Height + 4));
        new ToolTip().SetToolTip(currencyFilter, "Only show listings priced in one currency");
        listTop.Controls.Add(currencyFilter, 1, 0);
        list.Controls.Add(listTop, 0, 0);
        listings.Margin = new Padding(0, 0, 0, S(8));
        list.Controls.Add(listings, 0, 1);
        var listBottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        listBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        listBottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        listingHint.Anchor = AnchorStyles.Left;
        listingHint.AutoEllipsis = true;
        listBottom.Controls.Add(listingHint, 0, 0);
        listBottom.Controls.Add(open, 1, 0);
        list.Controls.Add(listBottom, 0, 2);
        listCard.Controls.Add(list);
        page.Controls.Add(listCard, 1, 2);
        return page;
    }

    /// <summary>The dropdown shows the chosen currency, and its menu lists each with its icon.</summary>
    private void RefreshCurrencyFilter()
    {
        var entry = GamePriceOverlay.CurrencyFilters.First(option => option.Id == listingCurrency);
        currencyFilter.Icon = entry.Id is null ? null : CurrencyIcons.Get(entry.Id);
        currencyFilter.Text = $"{entry.Short}  ▾";
        currencyFilter.Selected = entry.Id is not null;
        foreach (ToolStripMenuItem item in currencyMenu.Items)
        {
            item.Checked = Equals(item.Tag, listingCurrency);
            if (item.Tag is string id && item.Image is null) item.Image = CurrencyIcons.Get(id);
        }
    }

    private void OnThemeChanged()
    {
        WindowTheme.Repaint(this);
        // Hidden pages are not children of the window, so repaint them too.
        WindowTheme.Repaint(pricePage);
        foreach (var page in new[] { pages.Trades, pages.Currency, pages.Settings }) WindowTheme.Repaint(page);
    }

    private void ShowPage(Control page)
    {
        pageHost.SuspendLayout();
        pageHost.Controls.Clear();
        page.Dock = DockStyle.Fill;
        pageHost.Controls.Add(page);
        pageHost.ResumeLayout();
        foreach (var (button, target) in navigation) button.Selected = target == page;
        if (page == pages.Currency) pages.CurrencyShown();
        if (page == pages.Trades) pages.TradesShown();
    }

    private void UpdateGameStatus()
    {
        bool running = GameHotkey.FindClientLog() is not null || GameHotkey.ForegroundGame() != 0;
        statusDot.ForeColor = running ? WindowTheme.Glow : WindowTheme.Disabled;
        statusDot.Invalidate();
        statusTitle.Text = running ? "Overlay armed" : "Waiting for the game";
        statusMeta.Text = $"{preferences.Shortcut} on hover";
    }

    /// <summary>
    /// Asked once. ExileWorth cannot start itself when Path of Exile opens, so
    /// starting quietly at sign-in is how the shortcut is always ready.
    /// </summary>
    private Control BuildStartupBanner()
    {
        int S(int value) => value * DeviceDpi / 96;
        var host = new Panel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = WindowTheme.Background, Padding = new Padding(S(24), S(16), S(24), 0),
            Visible = preferences.StartupPromptAnswered != true && !Startup.IsEnabled(),
        };
        var card = new Card { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(S(16), S(12), S(12), S(12)), Margin = Padding.Empty };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var text = Ui.Stack();
        text.Add(Ui.Label("Start ExileWorth when your computer starts?", WindowTheme.Sans(10f, FontStyle.Bold), WindowTheme.Foreground)).Margin = Padding.Empty;
        var hint = text.Add(Ui.Label(
            "It waits quietly in the notification area, so the price-check shortcut is ready whenever Path of Exile is in front. " +
            "You can change this any time in Settings.", WindowTheme.Sans(8.5f), WindowTheme.Muted, wrap: true));
        hint.Margin = new Padding(0, S(2), S(12), 0);
        var turnOn = new FlatButton(ButtonKind.Primary, "Turn on") { Anchor = AnchorStyles.Right };
        var notNow = new FlatButton(ButtonKind.Ghost, "Not now") { Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        layout.Controls.Add(text, 0, 0);
        layout.Controls.Add(turnOn, 1, 0);
        layout.Controls.Add(notNow, 2, 0);
        card.Controls.Add(layout);
        host.Controls.Add(card);

        void Answer(bool enable)
        {
            if (enable)
            {
                try { Startup.SetEnabled(true); }
                catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                {
                    hint.Text = "Windows did not allow it. Try Start with Windows in Settings.";
                    hint.ForeColor = WindowTheme.Down;
                    return;
                }
            }
            try { SavePreferences(preferences with { StartupPromptAnswered = true }); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            pages.SyncStartup(preferences);
            // Hide after the click returns; never dispose a button inside its handler.
            BeginInvoke(() => host.Visible = false);
        }
        turnOn.Click += (_, _) => Answer(true);
        notNow.Click += (_, _) => Answer(false);
        return host;
    }

    private void SavePreferences(DesktopPreferences updated)
    {
        updated.Save();
        bool themeChanged = updated.Theme != preferences.Theme;
        preferences = updated;
        hotkey?.SetShortcut(updated.Shortcut);
        if (themeChanged) WindowTheme.ApplyTheme(updated.Theme);
        if (pricePage.Controls.Find("shortcutHint", true).FirstOrDefault() is Label hint)
            hint.Text = $"Hover an item in Path of Exile and press {updated.Shortcut}.";
        UpdateGameStatus();
        if (!hasItem && !busy) ShowShortcutHint();
    }

    private void ShowShortcutHint()
    {
        statusText = $"Hover an item in Path of Exile and press {preferences.Shortcut}, or copy it and use Check clipboard.";
        Render();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (startHidden && value)
        {
            startHidden = false;
            if (!IsHandleCreated) CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BeginInvoke(async () =>
        {
            _ = CurrencyIcons.LoadAsync(pages.Worker, ["chaos", "divine", "alch", "exalted"]);
            await RunAsync(LoadLeaguesAsync);
        });
        WindowTheme.ApplyTitleBar(Handle, WindowTheme.Background);
        hotkey = new GameHotkey(Handle, preferences.Shortcut, active =>
        {
            if (!active && overlay is { HasKeyboardFocus: false }) overlay.Hide();
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
            if (game != 0 && !busy && CooldownSeconds == 0) _ = CaptureAsync(game);
        }
        base.WndProc(ref m);
    }

    private Task CaptureAsync(nint game) => RunAsync(async () =>
    {
        statusText = "Copying item from Path of Exile…";
        currentListings = [];
        Render();
        string? text;
        try { text = await GameHotkey.CopyItemAsync(game, preferences.Shortcut.Key, preferences.AdvancedItemCopy != true); }
        finally
        {
            if (!IsDisposed && GameHotkey.ForegroundGame() == game)
            {
                overlay.Open(game, preferences.NearCursor);
                Render();
            }
        }
        if (IsDisposed) return;
        if (text is null)
        {
            statusText = "Item copy cancelled. Release the shortcut keys and try again.";
            return;
        }
        await CheckAsync(text, null);
    });

    private async Task RunAsync(Func<Task> operation)
    {
        if (busy || CooldownSeconds > 0) return;
        busy = true;
        SetControlsEnabled();
        try
        {
            await operation();
        }
        catch (Exception error)
        {
            if (IsDisposed) return;
            if (error is TradeException { Code: "rate_limited", RetryAfterMs: > 0 } limited)
            {
                cooldownUntil = Environment.TickCount64 + (long)Math.Ceiling(limited.RetryAfterMs.Value);
                // The engine parses the copied item before contacting the API and
                // retains it, so Search can retry even if the first lookup failed.
                if (copiedItem is not null) hasItem = true;
                statusText = $"Trade API cooldown · {CooldownSeconds}s remaining. Requests are paused.";
                cooldownTimer.Start();
            }
            else statusText = error.Message;
        }
        finally
        {
            busy = false;
            if (!IsDisposed)
            {
                SetControlsEnabled();
                Render();
            }
        }
    }

    private void SetControlsEnabled()
    {
        league.Enabled = paste.Enabled = !busy && CooldownSeconds == 0;
        currencyFilter.Enabled = !busy && CooldownSeconds == 0;
        search.Enabled = hasItem && !busy && CooldownSeconds == 0 && filters.HasValidMinimums;
        search.Text = CooldownSeconds > 0 ? $"Wait {CooldownSeconds}s" : busy && hasItem ? "Searching…" : exchange is not null ? "Refresh" : "Search";
        open.Enabled = tradeUrl is not null && !busy;
    }

    /// <summary>Pushes the current state to the desktop page and the overlay.</summary>
    private void Render()
    {
        string title = itemName ?? (hasItem ? "Unknown item" : "No item checked yet");
        hero.Show(title, exchange is not null ? meta : statusText, typical, total, exchange is not null);
        listings.Exchange = exchange is not null;
        pricesHeading.Text = exchange is null ? "TRADE LISTINGS" : "EXCHANGE OFFERS";
        modifiersHeading.Text = exchange is null ? "MATCH MODIFIERS" : "EXCHANGE INFO";
        currencyFilter.Visible = exchange is null;
        open.Text = exchange is null ? "Open trade site ↗" : "Open exchange site ↗";
        open.AccessibleName = open.Text;
        listingHint.Text = currentListings.Any(listing => listing.Whisper is not null)
            ? "Click a listing to copy its whisper." : "";
        if (!ReferenceEquals(listings.Listings, currentListings)) listings.Listings = currentListings;
        listings.Placeholder = busy ? "Searching…" : exchange?.Item.Id == "chaos" ? "Chaos is the reference currency: 1 Chaos Orb = 1 chaos." : exchange is not null ? "No exchange offers in Chaos. The source remains Exchange; try again later." : hasItem ? "No asking prices matched. Try fewer modifiers or All currencies."
            : "Listings appear here after a price check.";
        if (overlay.Visible)
        {
            overlay.Present(new PriceView(itemName ?? "ExileWorth", meta.Length > 0 ? meta : statusText, currentListings,
                currentFilters, typical, total, listingCurrency, busy, hasItem, tradeUrl, CooldownSeconds, exchange is not null), statusText, leagueName);
        }
    }

    private async Task SelectCurrencyAsync(string? id, string[]? selected = null, Dictionary<string, double>? minimums = null)
    {
        if (busy || listingCurrency == id) return;
        listingCurrency = id;
        RefreshCurrencyFilter();
        if (hasItem) await RunAsync(() => CheckAsync(null, selected ?? filters.Selected, minimums ?? filters.Minimums));
        else Render();
    }

    private async Task LoadLeaguesAsync()
    {
        statusText = "Loading trade leagues…";
        Render();
        var data = await worker.SendAsync<LeagueResult>(new { action = "leagues", preferred });
        if (IsDisposed) return;
        SetLeagues(data.Leagues, data.Selected);
        ShowShortcutHint();
    }

    private async Task CheckAsync(string? text, string[]? enabled, Dictionary<string, double>? minimums = null)
    {
        tradeUrl = null;
        if (text is not null)
        {
            copiedItem = text;
            hasItem = false;
            itemName = null;
            filters.SetItems([]);
            currentFilters = [];
        }
        typical = null;
        exchange = null;
        total = 0;
        meta = "";
        currentListings = [];
        statusText = "Checking item or exchange prices…";
        Render();
        var result = await worker.SendAsync<PriceResult>(new { action = "check", preferred, text, enabled, minimums, currency = listingCurrency });
        if (IsDisposed) return;
        SetLeagues(result.Leagues, result.Selected);
        hasItem = true;
        filters.SetItems(result.Filters);
        currentFilters = result.Filters;
        itemName = result.ItemName ?? "Price Check";
        typical = result.Typical;
        exchange = result.Exchange;
        total = result.Total;
        currentListings = result.Listings;
        _ = CurrencyIcons.LoadAsync(pages.Worker, currentListings.Select(listing => listing.Currency).Distinct());
        if (Uri.TryCreate(result.TradeUrl, UriKind.Absolute, out var url) &&
            url.Scheme == Uri.UriSchemeHttps && url.Host == "www.pathofexile.com")
            tradeUrl = url;
        if (exchange is not null)
        {
            meta = exchange.PerItemChaos is null ? "Exchange · no offers in Chaos"
                : exchange.Quantity > 1 && exchange.StackChaos is double stack
                    ? $"Stack {exchange.Quantity} ≈ {WindowTheme.ExchangeAmount(stack)} chaos" : "Exchange · bulk offers";
            var checkedAt = DateTimeOffset.FromUnixTimeMilliseconds(exchange.UpdatedAt).ToLocalTime();
            statusText = $"Bulk asking rates · {result.Total} offers · checked {checkedAt:HH:mm} · cached up to 5 min. Not live in-game orders.";
            if (exchange.Item.Id == "chaos") statusText = "Exchange · Chaos is the reference currency: 1 Chaos Orb = 1 chaos.";
            filters.Placeholder = exchange.Item.Id == "chaos" ? "Chaos is the reference currency: 1 Chaos Orb = 1 chaos."
                : $"Bulk Item Exchange asking rates.\nChecked {checkedAt:HH:mm} · cached up to 5 min.\nSellers may require bulk purchases.\nNot live in-game exchange orders.";
            return;
        }
        filters.Placeholder = "Modifiers you can match appear here.";
        string currency = GamePriceOverlay.CurrencyFilters.First(entry => entry.Id == listingCurrency).Short;
        string scope = listingCurrency is null ? result.Selected : $"{result.Selected} · {currency} only";
        meta = result.Total == 0 ? $"Trade · {scope} · no matches" : $"Trade · {scope} · {result.Total:N0} matches";
        statusText = result.Total == 0
            ? $"Trade · no matches in {scope}. Try All currencies or fewer modifiers."
            : $"Trade · {scope} · {result.Total:N0} matches · showing the {result.Listings.Length} cheapest asking prices.";
    }

    private void CopyWhisper(Listing listing)
    {
        if (listing.Whisper is null) return;
        try
        {
            Clipboard.SetText(listing.Whisper);
            listingHint.Text = $"Whisper to {listing.AccountName} copied. Paste it in game chat.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            listingHint.Text = "The clipboard is busy. Try again.";
        }
    }

    private void SetLeagues(League[] available, string? selected)
    {
        pages.SetLeagues(available, selected);
        var current = available.FirstOrDefault(entry => entry.Id == selected);
        leagueName = current?.Text ?? selected ?? "";
        league.Text = current is null ? "League ▾" : $"{current.Text}  ▾";
        var items = leagueMenu.Items.OfType<ToolStripMenuItem>().ToArray();
        if (!items.Select(item => (item.Tag as League)?.Id).SequenceEqual(available.Select(entry => (string?)entry.Id)))
        {
            while (leagueMenu.Items.Count > 0) leagueMenu.Items[0].Dispose();
            foreach (var entry in available)
            {
                var option = new ToolStripMenuItem(entry.Text) { Tag = entry };
                option.Click += async (_, _) => await SelectLeague(entry);
                leagueMenu.Items.Add(option);
            }
            items = leagueMenu.Items.OfType<ToolStripMenuItem>().ToArray();
        }
        foreach (var item in items) item.Checked = (item.Tag as League)?.Id == selected;
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
        if (hasItem) await RunAsync(() => CheckAsync(null, filters.ItemCount == 0 ? null : filters.Selected, filters.Minimums));
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
            CurrencyIcons.Changed -= RefreshCurrencyFilter;
            WindowTheme.ThemeChanged -= OnThemeChanged;
            currencyMenu.Dispose();
            gameWatch.Dispose();
            cooldownTimer.Dispose();
            tray?.Dispose();
            menu.Dispose();
            leagueMenu.Dispose();
            pages?.Dispose();
            overlay?.Dispose();
            worker.Dispose();
        }
        base.Dispose(disposing);
    }
}
