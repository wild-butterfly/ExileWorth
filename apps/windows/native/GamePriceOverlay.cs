using System.Diagnostics;

namespace ExileWorth.Windows;

/// <summary>What the compact overlay shows for the current price check.</summary>
internal sealed record PriceView(
    string Title, string Meta, IReadOnlyList<Listing> Listings, TypicalPrice? Typical,
    int Total, string? Currency, bool Busy, Uri? TradeUrl);

/// <summary>
/// The compact in-game panel. It never takes focus: every control is painted
/// or non-focusable, and clicks arrive through MA_NOACTIVATE so the game keeps
/// the keyboard.
/// </summary>
internal sealed class GamePriceOverlay : Form
{
    public static readonly (string Label, string? Id, string Short)[] CurrencyFilters =
        [("All currencies", null, "All"), ("Orb of Alchemy", "alch", "Alch"), ("Chaos Orb", "chaos", "Chaos"),
         ("Divine Orb", "divine", "Divine")];

    private readonly Func<string> thanks;
    private readonly Worker tradeWorker = new();
    private readonly FlatButton priceTab = new(ButtonKind.Tab, "Price") { TabStop = false };
    private readonly FlatButton tradeTab = new(ButtonKind.Tab, "Trades") { TabStop = false };
    private readonly Label league = new()
    {
        AutoSize = true, Font = WindowTheme.Mono(7.5f), ForeColor = WindowTheme.Dim,
        Anchor = AnchorStyles.Right, UseMnemonic = false, Margin = new Padding(0, 0, 6, 0),
    };
    private readonly PriceHeader header = new(compact: true) { Dock = DockStyle.Fill, BackColor = WindowTheme.Overlay };
    private readonly ListingView listings = new(compact: true)
    {
        Dock = DockStyle.Fill, BackColor = WindowTheme.Overlay, ShowLevel = false,
        Placeholder = "Hover an item in Path of Exile and press the shortcut.",
    };
    private readonly List<FlatButton> currencyChips = [];
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
    private nint game;
    private Uri? tradeUrl;
    private bool loadingTrades, sending;
    private Point? drag;

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

    public GamePriceOverlay(Action<string?> selectCurrency, Func<string> thanks)
    {
        this.thanks = thanks;
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

        // Price page: header, small currency icons, listings, status line.
        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(S(10), S(2), S(8), 0),
            Margin = Padding.Empty,
        };
        var tip = new ToolTip { InitialDelay = 250, ShowAlways = true };
        foreach (var (label, id, _) in CurrencyFilters)
        {
            var chip = new FlatButton(ButtonKind.Chip, id is null ? "All" : "")
            {
                TabStop = false, Tag = id, AccessibleName = id is null ? "All currencies" : $"Only {label} listings",
            };
            tip.SetToolTip(chip, id is null ? "Listings in any currency" : $"Only listings priced in {label}");
            chip.Click += (_, _) => selectCurrency(id);
            currencyChips.Add(chip);
            filters.Controls.Add(chip);
        }
        var spacer = new Panel { Width = S(8), Height = 1, Margin = Padding.Empty };
        filters.Controls.Add(spacer);
        tradeSite.Click += (_, _) =>
        {
            if (tradeUrl is not null) Process.Start(new ProcessStartInfo(tradeUrl.AbsoluteUri) { UseShellExecute = true });
        };
        filters.Controls.Add(tradeSite);
        tip.SetToolTip(tradeSite, "Open this search on pathofexile.com");

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(8), S(4), S(4), 0) };
        listHost.Controls.Add(listings);
        listings.RowClicked += (index, _) =>
        {
            var listing = listings.Listings[index];
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
        };

        var priceLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        priceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        priceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(56)));
        priceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(30)));
        priceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        priceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(26)));
        priceLayout.Controls.Add(header, 0, 0);
        priceLayout.Controls.Add(filters, 0, 1);
        priceLayout.Controls.Add(listHost, 0, 2);
        priceLayout.Controls.Add(Footer(status), 0, 3);
        EnableDrag(header);
        pricePage.Controls.Add(priceLayout);

        // Trades page: whispers with one-press commands.
        var tradeHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(8), S(6), S(4), 0) };
        tradeHost.Controls.Add(trades);
        var refresh = new FlatButton(ButtonKind.Chip, "↻ Refresh") { TabStop = false };
        var hideout = new FlatButton(ButtonKind.Chip, "Hideout") { TabStop = false };
        refresh.Click += async (_, _) => await LoadTradesAsync();
        hideout.Click += async (_, _) => await SendAsync("/hideout", "Hideout");
        trades.CommandChosen += async (trade, command, label) => await SendAsync(command, $"{label} {trade.Character}");
        var tradeBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(S(10), S(4), S(8), 0), Margin = Padding.Empty,
        };
        tradeBar.Controls.AddRange([refresh, hideout]);
        var tradeLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        tradeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tradeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(32)));
        tradeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tradeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(26)));
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
        Controls.Add(root);
        ClientSize = new Size(S(420), S(372));
        ShowTrades(false);
        CurrencyIcons.Changed += RefreshIcons;
        RefreshIcons();
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

    private void RefreshIcons()
    {
        // Icon-only once the art arrives; a short name until then (or if offline).
        foreach (var chip in currencyChips)
        {
            if (chip.Tag is not string id) continue;
            chip.Icon = CurrencyIcons.Get(id) ?? chip.Icon;
            chip.Text = chip.Icon is not null ? "" : CurrencyFilters.First(entry => entry.Id == id).Short;
        }
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
        header.Show(view.Title, view.Meta, view.Typical, view.Total);
        league.Text = leagueName ?? "";
        if (!ReferenceEquals(listings.Listings, view.Listings)) listings.Listings = view.Listings;
        listings.Placeholder = view.Busy ? "Searching…" : message;
        status.Text = message;
        status.ForeColor = WindowTheme.Dim;
        tradeUrl = view.TradeUrl;
        tradeSite.Enabled = tradeUrl is not null && !view.Busy;
        foreach (var chip in currencyChips)
        {
            chip.Selected = Equals(chip.Tag, view.Currency);
            chip.Enabled = !view.Busy;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0021) // WM_MOUSEACTIVATE
        {
            message.Result = 3; // MA_NOACTIVATE: accept clicks while the game keeps focus.
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
            var result = await TradeLog.ReadAsync(tradeWorker, GameHotkey.ClientLog(game), thanks());
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
            CurrencyIcons.Changed -= RefreshIcons;
            tradeWorker.Dispose();
        }
        base.Dispose(disposing);
    }
}
