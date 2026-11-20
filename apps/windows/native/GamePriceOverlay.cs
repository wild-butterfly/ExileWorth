using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ExileWorth.Windows;

/// <summary>What the compact overlay shows for the current price check.</summary>
internal sealed record PriceView(
    string Title, string Meta, IReadOnlyList<Listing> Listings, IReadOnlyList<Modifier> Filters,
    TypicalPrice? Typical, int Total, string? Currency, bool Busy, bool HasItem, Uri? TradeUrl);

/// <summary>
/// The compact in-game panel. It opens without focus; clicking a minimum input
/// temporarily activates it for typing. Other clicks keep the game focused.
/// </summary>
internal sealed class GamePriceOverlay : Form
{
    public static readonly (string Label, string? Id, string Short)[] CurrencyFilters =
    [
        ("All currencies", null, "All"), ("Orb of Alchemy", "alch", "Alch"), ("Chaos Orb", "chaos", "Chaos"),
        ("Exalted Orb", "exalted", "Exalted"), ("Divine Orb", "divine", "Divine"),
    ];

    private const int MaxModifierRows = 4;
    public bool HasKeyboardFocus => IsHandleCreated && GetForegroundWindow() == Handle;

    private readonly Func<DesktopPreferences> preferences;
    private readonly Worker tradeWorker = new();
    private readonly FlatButton priceTab = new(ButtonKind.Tab, "Price") { TabStop = false };
    private readonly FlatButton tradeTab = new(ButtonKind.Tab, "Trades") { TabStop = false };
    private readonly Label league = new()
    {
        AutoSize = true, Font = WindowTheme.Mono(7.5f), ForeColor = WindowTheme.Dim,
        Anchor = AnchorStyles.Right, UseMnemonic = false, Margin = new Padding(0, 0, 6, 0),
    };
    private readonly PriceHeader header = new(compact: true) { Dock = DockStyle.Fill, BackColor = WindowTheme.Overlay };
    private readonly ModifierView modifiers = new(compact: true) { Dock = DockStyle.Fill, BackColor = WindowTheme.Overlay };
    private readonly ListingView listings = new(compact: true)
    {
        Dock = DockStyle.Fill, BackColor = WindowTheme.Overlay, ShowLevel = false,
        Placeholder = "Hover an item in Path of Exile and press the shortcut.",
    };
    private readonly FlatButton currency = new(ButtonKind.Chip, "All  ▾") { TabStop = false, AccessibleName = "Listing currency" };
    private readonly CurrencyPicker picker = new();
    private readonly FlatButton preset = new(ButtonKind.Chip, "Exact  ▾") { TabStop = false };
    private readonly PresetPicker presetPicker = new();
    private readonly FlatButton combinedResistance = new(ButtonKind.Chip, "Total res") { TabStop = false, Visible = false };
    private int? selectedPreset;
    private readonly FlatButton search = new(ButtonKind.Accent, "Search") { TabStop = false, Enabled = false };
    private readonly FlatButton tradeSite = new(ButtonKind.Chip, "Trade site ↗") { TabStop = false, Enabled = false };
    private readonly Label status = new()
    {
        Dock = DockStyle.Fill, AutoEllipsis = true, Font = WindowTheme.Sans(8f),
        ForeColor = WindowTheme.Dim, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
    };
    private readonly TradeView trades = new(compact: true)
    {
        Dock = DockStyle.Fill, BackColor = WindowTheme.Overlay,
        Placeholder = "No recent trade whispers.",
    };
    private readonly Label tradeStatus = new()
    {
        Dock = DockStyle.Fill, AutoEllipsis = true, Font = WindowTheme.Sans(8f),
        ForeColor = WindowTheme.Dim, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
    };
    private readonly Panel pricePage = new() { Dock = DockStyle.Fill };
    private readonly Panel tradePage = new() { Dock = DockStyle.Fill };
    private readonly TableLayoutPanel priceLayout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
    private IReadOnlyList<Modifier>? shownFilters;
    private string? selectedCurrency;
    private nint game;
    private Uri? tradeUrl;
    private bool loadingTrades, sending, busy, hasItem;
    private Point? drag;
    private MouseHook? mouseCallback;
    private nint mouseHook;
    private MouseHook? keyboardCallback;
    private nint keyboardHook;

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            StopWatchingInput();
            return;
        }
        mouseCallback ??= ObserveClick;
        keyboardCallback ??= ObserveEscape;
        nint module = GetModuleHandle(null);
        if (mouseHook == 0) mouseHook = SetWindowsHookEx(14, mouseCallback, module, 0); // WH_MOUSE_LL
        if (keyboardHook == 0) keyboardHook = SetWindowsHookEx(13, keyboardCallback, module, 0); // WH_KEYBOARD_LL
    }

    /// <summary>
    /// Watches clicks anywhere on screen without consuming them, so a click on
    /// the game both closes this panel and still reaches the game.
    /// </summary>
    private nint ObserveClick(int code, nint message, nint data)
    {
        if (code >= 0 && (message == 0x0201 || message == 0x0204 || message == 0x0207) && IsHandleCreated)
        {
            // Low-level hook points are physical pixels. Asking which window is
            // under the point stays correct when monitors use different scaling,
            // where comparing them with Bounds (logical pixels) would not.
            var point = new NativePoint(Marshal.ReadInt32(data), Marshal.ReadInt32(data, sizeof(int)));
            nint target = WindowFromPhysicalPoint(point);
            bool inside = target != 0 && GetAncestor(target, 2) == Handle; // GA_ROOT
            if (!inside) BeginInvoke(() => { if (Visible) Hide(); });
        }
        return CallNextHookEx(0, code, message, data);
    }

    private nint ObserveEscape(int code, nint message, nint data)
    {
        if (code >= 0 && message == 0x0100 && Marshal.ReadInt32(data) == (int)Keys.Escape && IsHandleCreated && !(HasKeyboardFocus && modifiers.Editing))
            BeginInvoke(() => { if (Visible) Hide(); });
        return CallNextHookEx(0, code, message, data);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (IsHandleCreated) BeginInvoke(() =>
        {
            if (Visible && !HasKeyboardFocus && GameHotkey.ForegroundGame() == 0) Hide();
        });
    }

    private void StopWatchingInput()
    {
        if (mouseHook != 0) UnhookWindowsHookEx(mouseHook);
        if (keyboardHook != 0) UnhookWindowsHookEx(keyboardHook);
        mouseHook = keyboardHook = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint WindowFromPhysicalPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    private delegate nint MouseHook(int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern nint SetWindowsHookEx(int id, MouseHook callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return parameters;
        }
    }

    public GamePriceOverlay(Action<string?, string[], Dictionary<string, double>> selectCurrency, Action<string[], Dictionary<string, double>> runSearch, Func<DesktopPreferences> preferences)
    {
        this.preferences = preferences;
        Text = "ExileWorth Price Overlay";
        AccessibleName = Text;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = WindowTheme.Overlay;
        ForeColor = WindowTheme.Foreground;
        Font = WindowTheme.Sans(9f);
        int S(int value) => value * DeviceDpi / 96;

        // Tab strip: mark, tabs, league, close. Empty chrome drags the panel.
        var strip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, BackColor = WindowTheme.Raised,
            Padding = new Padding(S(10), S(5), S(6), S(5)), Margin = Padding.Empty,
        };
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var mark = new BrandMark(S(18)) { Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, S(8), 0) };
        var close = new FlatButton(ButtonKind.Icon, "✕") { TabStop = false, AccessibleName = "Hide overlay", Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        priceTab.Anchor = tradeTab.Anchor = AnchorStyles.Left;
        strip.Controls.Add(mark, 0, 0);
        strip.Controls.Add(priceTab, 1, 0);
        strip.Controls.Add(tradeTab, 2, 0);
        strip.Controls.Add(league, 4, 0);
        strip.Controls.Add(close, 5, 0);
        strip.Paint += (_, e) =>
        {
            using var line = new Pen(WindowTheme.Hairline);
            e.Graphics.DrawLine(line, 0, strip.Height - 1, strip.Width, strip.Height - 1);
        };
        foreach (var handle in new Control[] { strip, mark, league }) EnableDrag(handle);
        close.Click += (_, _) => Hide();
        priceTab.Click += (_, _) => ShowTrades(false);
        tradeTab.Click += async (_, _) =>
        {
            ShowTrades(true);
            await LoadTradesAsync();
        };

        // Toolbar: currency dropdown and Search on the left, trade site on the right.
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Padding = new Padding(S(10), S(3), S(10), 0),
            Margin = Padding.Empty,
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        currency.Anchor = search.Anchor = AnchorStyles.Left;
        preset.Anchor = tradeSite.Anchor = AnchorStyles.Right;
        combinedResistance.Anchor = AnchorStyles.Left;
        toolbar.Controls.Add(currency, 0, 0);
        toolbar.Controls.Add(search, 1, 0);
        toolbar.Controls.Add(combinedResistance, 2, 0);
        toolbar.Controls.Add(preset, 4, 0);
        toolbar.Controls.Add(tradeSite, 5, 0);
        var tip = new ToolTip { InitialDelay = 250, ShowAlways = true };
        tip.SetToolTip(currency, "Only show listings priced in one currency");
        tip.SetToolTip(search, "Search again with the ticked modifiers");
        tip.SetToolTip(tradeSite, "Open this search on pathofexile.com");
        currency.Click += (_, _) =>
        {
            presetPicker.Visible = false;
            var below = PointToClient(currency.Parent!.PointToScreen(new Point(currency.Left, currency.Bottom + S(4))));
            picker.Toggle(below, selectedCurrency);
        };
        picker.Chosen += id => selectCurrency(id, modifiers.Selected, modifiers.Minimums);
        search.Click += (_, _) =>
        {
            if (!modifiers.HasValidMinimums) return;
            if (game != 0) SetForegroundWindow(game);
            runSearch(modifiers.Selected, modifiers.Minimums);
        };
        modifiers.EditingRequested += () => SetForegroundWindow(Handle);
        modifiers.EditingFinished += () => { if (game != 0) SetForegroundWindow(game); };
        modifiers.Changed += () =>
        {
            UpdateSearch();
            UpdateModifierTools();
            tradeSite.Enabled = false;
            status.Text = "Filters changed · Search to update prices.";
        };
        tip.SetToolTip(modifiers, "Type a minimum beside a modifier, then Search. Enter confirms; Escape restores the item value.");
        tradeSite.Click += (_, _) =>
        {
            if (tradeUrl is not null) Process.Start(new ProcessStartInfo(tradeUrl.AbsoluteUri) { UseShellExecute = true });
        };

        var modifierHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(10), S(4), S(6), 0), Margin = Padding.Empty };
        modifierHost.Controls.Add(modifiers);
        tip.SetToolTip(preset, "Lower checked search minimums from the original item values");
        preset.Click += (_, _) =>
        {
            picker.Visible = false;
            var below = PointToClient(preset.Parent!.PointToScreen(new Point(preset.Left, preset.Bottom + S(4))));
            below.X = Math.Clamp(below.X, 0, Math.Max(0, ClientSize.Width - S(112)));
            presetPicker.Toggle(below, selectedPreset);
        };
        presetPicker.Chosen += percent => modifiers.ApplyPreset(percent);
        combinedResistance.Font = WindowTheme.Sans(7.5f);
        combinedResistance.Click += (_, _) => modifiers.ToggleCombinedResistance();
        tip.SetToolTip(combinedResistance, "Combine fire + cold + lightning into one total elemental resistance filter. Click again for individual filters; hover the combined row for the breakdown.");

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(8), S(4), S(4), 0), Margin = Padding.Empty };
        listHost.Controls.Add(listings);
        listings.RowClicked += (index, _) => CopyWhisper(listings.Listings[index]);

        priceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        priceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, header.MeasuredHeight));
        priceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(30)));
        priceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        priceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        priceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(24)));
        priceLayout.Controls.Add(header, 0, 0);
        priceLayout.Controls.Add(toolbar, 0, 1);
        priceLayout.Controls.Add(modifierHost, 0, 2);
        priceLayout.Controls.Add(listHost, 0, 3);
        priceLayout.Controls.Add(Footer(status), 0, 4);
        EnableDrag(header);
        pricePage.Controls.Add(priceLayout);

        // Trades page: whispers with the macOS actions and prepared replies.
        var tradeHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(8), S(4), S(4), 0) };
        tradeHost.Controls.Add(trades);
        var refresh = new FlatButton(ButtonKind.Chip, "↻ Refresh") { TabStop = false };
        var hideout = new FlatButton(ButtonKind.Chip, "Go to hideout") { TabStop = false };
        refresh.Click += async (_, _) => await LoadTradesAsync();
        hideout.Click += async (_, _) => await SendAsync("/hideout", "Go to hideout");
        trades.CommandChosen += async (_, command, description) => await SendAsync(command, description);
        var tradeBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(S(10), S(3), S(8), 0), Margin = Padding.Empty,
        };
        tradeBar.Controls.AddRange([refresh, hideout]);
        var tradeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        tradeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tradeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(30)));
        tradeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tradeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(24)));
        tradeLayout.Controls.Add(tradeBar, 0, 0);
        tradeLayout.Controls.Add(tradeHost, 0, 1);
        tradeLayout.Controls.Add(Footer(tradeStatus), 0, 2);
        tradePage.Controls.Add(tradeLayout);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(38)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        body.Controls.AddRange([pricePage, tradePage]);
        root.Controls.Add(strip, 0, 0);
        root.Controls.Add(body, 0, 1);
        Controls.Add(presetPicker);
        Controls.Add(picker);
        Controls.Add(root);
        picker.BringToFront();
        presetPicker.BringToFront();
        // Any click outside the dropdown closes it.
        foreach (var control in new Control[] { strip, header, toolbar, modifiers, listings, trades, status, search, tradeSite, combinedResistance })
            control.MouseDown += (_, _) => { picker.Visible = false; presetPicker.Visible = false; };
        ClientSize = new Size(S(430), S(430));
        ShowTrades(false);
        CurrencyIcons.Changed += ShowCurrency;
        WindowTheme.ThemeChanged += OnThemeChanged;
        ShowCurrency();
    }

    private Control Footer(Label text)
    {
        var footer = new Panel { Dock = DockStyle.Fill, BackColor = WindowTheme.Sunken, Padding = new Padding(this.Px(12), 0, this.Px(10), 0), Margin = Padding.Empty };
        footer.Paint += (_, e) =>
        {
            using var line = new Pen(WindowTheme.Hairline);
            e.Graphics.DrawLine(line, 0, 0, footer.Width, 0);
        };
        text.BackColor = WindowTheme.Sunken;
        footer.Controls.Add(text);
        return footer;
    }

    /// <summary>The dropdown chip shows the chosen currency's icon and short name.</summary>
    private void ShowCurrency()
    {
        var entry = CurrencyFilters.First(option => option.Id == selectedCurrency);
        currency.Icon = entry.Id is null ? null : CurrencyIcons.Get(entry.Id);
        currency.Text = $"{entry.Short}  ▾";
        currency.Selected = entry.Id is not null;
    }

    private void UpdateSearch() => search.Enabled = hasItem && !busy && modifiers.ItemCount > 0 && modifiers.HasValidMinimums;

    private void UpdateModifierTools()
    {
        preset.Visible = modifiers.ItemCount > 0;
        preset.Enabled = !busy;
        combinedResistance.Enabled = !busy;
        combinedResistance.Visible = modifiers.HasCombinedResistance;
        combinedResistance.Selected = modifiers.CombinedResistance;
        selectedPreset = PresetPicker.Percentages.Cast<int?>().FirstOrDefault(percent => modifiers.MatchesPreset(percent!.Value));
        preset.Text = selectedPreset is 0 ? "Exact  ▾" : selectedPreset is int percent ? $"−{percent}%  ▾" : "Custom  ▾";
        preset.Selected = selectedPreset is > 0;
        int rows = Math.Min(modifiers.ItemCount, MaxModifierRows);
        priceLayout.RowStyles[2].Height = rows == 0 ? 0 : rows * this.Px(24) + this.Px(6);
    }

    private void OnThemeChanged()
    {
        if (IsHandleCreated) WindowTheme.ApplyPopupFrame(Handle);
        WindowTheme.Repaint(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowTheme.ApplyPopupFrame(Handle);
    }

    private void EnableDrag(Control handle)
    {
        handle.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) drag = Cursor.Position - (Size)Location; };
        handle.MouseMove += (_, _) => { if (drag is { } start) Location = Cursor.Position - (Size)start; };
        handle.MouseUp += (_, _) => drag = null;
    }

    public void Open(nint target, bool nearCursor)
    {
        game = target;
        ShowTrades(false);
        picker.Visible = false;
        presetPicker.Visible = false;
        if (Visible) return;
        Point cursor = Cursor.Position;
        Rectangle area = Screen.FromHandle(target).WorkingArea;
        int right = cursor.X + 22;
        int x = !nearCursor ? area.Right - Width - 12
            : right + Width <= area.Right ? right : cursor.X - Width - 16;
        int y = nearCursor ? cursor.Y + 16 : area.Top + 12;
        Location = new Point(
            Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        Show();
    }

    public void Present(PriceView view, string message, string? leagueName)
    {
        busy = view.Busy;
        hasItem = view.HasItem;
        header.Show(view.Title, view.Meta, view.Typical, view.Total);
        if (priceLayout.RowStyles[0].Height != header.MeasuredHeight) priceLayout.RowStyles[0].Height = header.MeasuredHeight;
        league.Text = leagueName ?? "";
        if (!ReferenceEquals(listings.Listings, view.Listings)) listings.Listings = view.Listings;
        listings.Placeholder = view.Busy ? "Searching…" : message;
        // Keep unsearched ticks until a new result replaces the modifiers.
        if (!ReferenceEquals(shownFilters, view.Filters))
        {
            shownFilters = view.Filters;
            modifiers.SetItems(view.Filters);
            UpdateModifierTools();
        }
        status.Text = message;
        status.ForeColor = WindowTheme.Dim;
        tradeUrl = view.TradeUrl;
        tradeSite.Enabled = tradeUrl is not null && !view.Busy;
        search.Text = view.Busy && view.HasItem ? "Searching…" : "Search";
        UpdateSearch();
        modifiers.Enabled = !view.Busy;
        preset.Enabled = combinedResistance.Enabled = !view.Busy;
        if (view.Busy) presetPicker.Visible = false;
        currency.Enabled = !view.Busy;
        selectedCurrency = view.Currency;
        ShowCurrency();
    }

    private void CopyWhisper(Listing listing)
    {
        if (listing.Whisper is null) return;
        try
        {
            Clipboard.SetText(listing.Whisper);
            status.Text = $"Whisper to {listing.AccountName} copied. In game: Enter, Ctrl+V.";
            status.ForeColor = WindowTheme.UpText;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            status.Text = "The clipboard is busy. Try again.";
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0021) // WM_MOUSEACTIVATE
        {
            message.Result = modifiers.InputAtCursor ? 1 : 3; // MA_ACTIVATE only for text inputs.
            return;
        }
        base.WndProc(ref message);
    }

    private void ShowTrades(bool show)
    {
        priceTab.Selected = !show;
        tradeTab.Selected = show;
        pricePage.Visible = !show;
        tradePage.Visible = show;
        picker.Visible = false;
        presetPicker.Visible = false;
    }

    private async Task SendAsync(string command, string description)
    {
        if (sending) return;
        sending = true;
        try
        {
            await GameHotkey.SendReplyAsync(game, command);
            if (!IsDisposed) SetTradeStatus($"Sent: {description}.", WindowTheme.UpText);
        }
        catch (InvalidOperationException error)
        {
            if (!IsDisposed) SetTradeStatus(error.Message, WindowTheme.Down);
        }
        finally { sending = false; }
    }

    private void SetTradeStatus(string text, Color color)
    {
        tradeStatus.Text = text;
        tradeStatus.ForeColor = color;
    }

    private async Task LoadTradesAsync()
    {
        if (loadingTrades) return;
        loadingTrades = true;
        SetTradeStatus("Reading recent trade whispers…", WindowTheme.Dim);
        try
        {
            var settings = preferences();
            var result = await TradeLog.ReadAsync(tradeWorker, GameHotkey.ClientLog(game), settings.Thanks, settings.Replies);
            if (IsDisposed) return;
            trades.Trades = result;
            int incoming = result.Count(trade => trade.Direction == "incoming");
            tradeTab.Badge = incoming > 0 ? (incoming > 9 ? "9+" : incoming.ToString()) : null;
            SetTradeStatus(result.Length == 0 ? "No recent trade whispers." : "Close game chat first. Each button sends one command.",
                WindowTheme.Dim);
        }
        catch (Exception error)
        {
            if (!IsDisposed) SetTradeStatus(error.Message, WindowTheme.Down);
        }
        finally { loadingTrades = false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopWatchingInput();
            CurrencyIcons.Changed -= ShowCurrency;
            WindowTheme.ThemeChanged -= OnThemeChanged;
            tradeWorker.Dispose();
        }
        base.Dispose(disposing);
    }
}
