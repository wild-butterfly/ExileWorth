using System.Diagnostics;
using Microsoft.Win32;

namespace ExileWorth.Windows;

internal sealed class DesktopPages : IDisposable
{
    private const string RepositoryUrl = "https://github.com/wild-butterfly/ExileWorth";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly (string Id, string Label)[] Denominations =
        [("chaos", "Chaos"), ("divine", "Divine"), ("exalted", "Exalted")];

    private readonly Worker tradeWorker = new();
    private readonly Func<string?> preferred;
    private readonly Func<DesktopPreferences> settings;
    private readonly Func<League, Task> selectLeague;
    private readonly Action<DesktopPreferences> save;
    private DesktopPreferences current;

    // Currency.
    private readonly CurrencyView rates = new() { Dock = DockStyle.Fill };
    private Label currencyLabel = null!;
    private readonly FlatButton refresh = new(ButtonKind.Primary, "Refresh");
    private readonly FlatButton denomination = new(ButtonKind.Secondary, "In Chaos  ▾");
    private readonly ContextMenuStrip denominationMenu = WindowTheme.Menu();
    private CurrencyResult? lastRates;
    private DateTime? ratesAt;
    private string? ratesLeague;
    private bool loadingRates;
    private int leagueRevision;

    // Trades.
    private readonly TradeView trades = new(compact: false) { Dock = DockStyle.Fill };
    private readonly Label tradeStatus = Ui.Label("", WindowTheme.Sans(9f), WindowTheme.Dim);
    private bool loadingTrades;

    // Settings.
    private readonly FlowLayoutPanel leagueChips = Ui.Row();
    private readonly Label feedback = Ui.Label("", WindowTheme.Sans(9f), WindowTheme.UpText);
    private string? selectedLeague;

    public Worker Worker { get; } = new();
    public Control Settings { get; }
    public Control Currency { get; }
    public Control Trades { get; }
    public League[] Available { get; private set; } = [];

    public DesktopPages(DesktopPreferences preferences, Action<DesktopPreferences> save,
        Func<string?> preferred, Func<League, Task> selectLeague, Func<DesktopPreferences> settings)
    {
        current = preferences;
        this.save = save;
        this.preferred = preferred;
        this.selectLeague = selectLeague;
        this.settings = settings;
        Currency = BuildCurrency();
        Trades = BuildTrades();
        Settings = BuildSettings();
    }

    // ── Shared page frame ────────────────────────────────────────────────

    private static TableLayoutPanel Page(string title, string subtitle, out Label subtitleLabel, params Control[] actions)
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, BackColor = WindowTheme.Background,
            Padding = new Padding(24, 20, 24, 20),
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 14) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titles = Ui.Stack();
        titles.Add(Ui.Label(title, WindowTheme.Display(18f), WindowTheme.Foreground)).Margin = Padding.Empty;
        subtitleLabel = titles.Add(Ui.Label(subtitle, WindowTheme.Sans(9f), WindowTheme.Muted));
        top.Controls.Add(titles, 0, 0);
        var row = Ui.Row(actions);
        row.Anchor = AnchorStyles.Right;
        top.Controls.Add(row, 1, 0);
        page.Add(top);
        return page;
    }

    private static Card Fill(Control content, Padding? padding = null)
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = padding ?? new Padding(12), Margin = new Padding(0, 0, 0, 10) };
        card.Controls.Add(content);
        return card;
    }

    // ── Currency ─────────────────────────────────────────────────────────

    private Control BuildCurrency()
    {
        var page = Page("Currency", "Refresh to load rates for the selected league.", out currencyLabel, denomination, refresh);
        var disclaimer = Ui.Label("Median of the cheapest live bulk offers: what sellers are asking, not what items sold for. " +
            "A low offer count means the median is soft.", WindowTheme.Sans(8.5f), WindowTheme.Dim, wrap: true);
        disclaimer.MaximumSize = new Size(720, 0);
        disclaimer.Margin = new Padding(0, 0, 0, 12);
        page.Add(disclaimer);
        page.Add(Fill(rates, new Padding(10, 6, 6, 6)), SizeType.Percent, 100);

        foreach (var (id, label) in Denominations)
        {
            var option = new ToolStripMenuItem(label) { Checked = id == "chaos", Tag = id };
            option.Click += (_, _) =>
            {
                foreach (ToolStripMenuItem item in denominationMenu.Items) item.Checked = item == option;
                denomination.Text = $"In {label}  ▾";
                rates.Denomination = id;
                ShowRates();
            };
            denominationMenu.Items.Add(option);
        }
        denomination.Click += (_, _) => denominationMenu.Show(denomination, new Point(0, denomination.Height + 4));
        refresh.Click += async (_, _) => await RefreshRatesAsync();
        rates.Placeholder = "Refresh to load rates.";
        return page;
    }

    public void CurrencyShown()
    {
        if (ratesLeague != selectedLeague && selectedLeague is not null && !loadingRates) _ = RefreshRatesAsync();
    }

    private async Task RefreshRatesAsync()
    {
        if (loadingRates) return;
        loadingRates = true;
        refresh.Enabled = false;
        refresh.Text = "Loading…";
        currencyLabel.Text = "Loading bulk exchange rates… this takes about a minute to respect GGG's rate limits.";
        rates.Placeholder = "Loading rates…";
        int requested = leagueRevision;
        try
        {
            var result = await Worker.SendAsync<CurrencyResult>(new { action = "currency", preferred = preferred() },
                TimeSpan.FromMinutes(3));
            if (Currency.IsDisposed || leagueRevision != requested) return;
            lastRates = result;
            ratesAt = DateTime.Now;
            ratesLeague = selectedLeague;
            _ = CurrencyIcons.LoadAsync(Worker, result.Rows.Select(row => row.Id).Append("chaos"));
            ShowRates();
        }
        catch (Exception error)
        {
            if (!Currency.IsDisposed && leagueRevision == requested) currencyLabel.Text = error.Message;
        }
        finally
        {
            loadingRates = false;
            if (!Currency.IsDisposed)
            {
                refresh.Enabled = true;
                refresh.Text = "Refresh";
                rates.Placeholder = "No rates loaded.";
            }
        }
    }

    private void ShowRates()
    {
        if (lastRates is null) return;
        rates.SetRows(lastRates.Rows);
        string unit = Denominations.First(entry => entry.Id == rates.Denomination).Label;
        bool missing = rates.Denomination != "chaos" && lastRates.Rows.FirstOrDefault(row => row.Id == rates.Denomination)?.Rate is null;
        currencyLabel.Text = missing
            ? $"{lastRates.Selected} · the {unit} rate is unavailable. Try refreshing."
            : $"{lastRates.Selected} · priced in {unit} · updated {ratesAt:t}";
    }

    // ── Trades ───────────────────────────────────────────────────────────

    private Control BuildTrades()
    {
        var reload = new FlatButton(ButtonKind.Primary, "Refresh");
        var page = Page("Trades", "Recent trade whispers from Path of Exile's chat log.", out _, reload);
        page.Add(Fill(trades, new Padding(10, 6, 6, 6)), SizeType.Percent, 100);
        tradeStatus.Text = "Buttons here copy the chat command. The in-game overlay's Trades tab types it for you.";
        page.Add(tradeStatus);
        trades.Placeholder = "No recent trade whispers. Start Path of Exile and refresh.";
        reload.Click += async (_, _) => await LoadTradesAsync();
        trades.CommandChosen += (trade, command, label) =>
        {
            try
            {
                Clipboard.SetText(command);
                SetTradeStatus($"Copied “{command}”. In game: Enter, Ctrl+V, Enter.", WindowTheme.UpText);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                SetTradeStatus("The clipboard is busy. Try again.", WindowTheme.Down);
            }
        };
        return page;
    }

    public void TradesShown() => _ = LoadTradesAsync();

    private void SetTradeStatus(string text, Color color)
    {
        tradeStatus.Text = text;
        tradeStatus.ForeColor = color;
    }

    private async Task LoadTradesAsync()
    {
        if (loadingTrades) return;
        loadingTrades = true;
        try
        {
            var result = await TradeLog.ReadAsync(tradeWorker, GameHotkey.FindClientLog(), settings().Thanks, settings().Replies);
            if (Trades.IsDisposed) return;
            trades.Trades = result;
            SetTradeStatus(result.Length == 0 ? "No recent trade whispers."
                : "Buttons here copy the chat command. The in-game overlay's Trades tab types it for you.", WindowTheme.Dim);
        }
        catch (Exception error)
        {
            if (!Trades.IsDisposed) SetTradeStatus(error.Message, WindowTheme.Muted);
        }
        finally { loadingTrades = false; }
    }

    // ── Settings ─────────────────────────────────────────────────────────

    private Control BuildSettings()
    {
        var page = Page("Settings", "Changes apply immediately.", out _);
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        var stack = Ui.Stack();
        stack.Dock = DockStyle.Top;
        stack.MaximumSize = new Size(760, 0);
        stack.Padding = new Padding(0, 0, 12, 0);

        // Shortcut.
        var modifier = new FlatButton(ButtonKind.Secondary);
        var key = new FlatButton(ButtonKind.Secondary);
        var modifierMenu = WindowTheme.Menu();
        var keyMenu = WindowTheme.Menu();
        keyMenu.MaximumSize = new Size(0, 420);
        var reset = new FlatButton(ButtonKind.Ghost, "Reset to default");
        void ShowShortcut()
        {
            modifier.Text = $"{Shortcut.FormatModifiers(current.Shortcut.Modifiers)}  ▾";
            key.Text = $"{current.Shortcut.Key}  ▾";
            foreach (ToolStripMenuItem item in modifierMenu.Items) item.Checked = Equals(item.Tag, current.Shortcut.Modifiers);
            foreach (ToolStripMenuItem item in keyMenu.Items) item.Checked = Equals(item.Tag, current.Shortcut.Key);
            reset.Enabled = current.Shortcut != Shortcut.Default;
        }
        foreach (uint option in Shortcut.ModifierOptions)
            modifierMenu.Items.Add(new ToolStripMenuItem(Shortcut.FormatModifiers(option), null,
                (_, _) => Apply(current with { Shortcut = current.Shortcut with { Modifiers = option } }, ShowShortcut)) { Tag = option });
        foreach (Keys option in Shortcut.KeyOptions)
            keyMenu.Items.Add(new ToolStripMenuItem(option.ToString(), null,
                (_, _) => Apply(current with { Shortcut = current.Shortcut with { Key = option } }, ShowShortcut)) { Tag = option });
        modifier.Click += (_, _) => modifierMenu.Show(modifier, new Point(0, modifier.Height + 4));
        key.Click += (_, _) => keyMenu.Show(key, new Point(0, key.Height + 4));
        reset.Click += (_, _) => Apply(current with { Shortcut = Shortcut.Default }, ShowShortcut);
        ShowShortcut();
        var plus = Ui.Label("+", WindowTheme.Sans(10f), WindowTheme.Faint);
        plus.Anchor = AnchorStyles.None;
        plus.Margin = new Padding(0, 0, 8, 0);
        stack.Add(Ui.Section("Price-check shortcut",
            "Press it while hovering an item in Path of Exile. It only works while the game is in front, " +
            "and ExileWorth copies the item once. Shortcuts that use Alt may trigger the game’s Pin Popups binding.",
            Ui.Row(modifier, plus, key, reset)));

        // Trade replies.
        var thanksField = new TextField(DesktopPreferences.DefaultThanks) { Width = 420 };
        thanksField.Input.Text = current.ThanksMessage ?? "";
        thanksField.Input.MaxLength = 120;
        var debounce = new System.Windows.Forms.Timer { Interval = 600 };
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            Apply(current with { ThanksMessage = thanksField.Input.Text.Trim() is { Length: > 0 } text ? text : null });
        };
        thanksField.Input.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
        var repliesList = Ui.Stack();
        repliesList.Dock = DockStyle.Top;
        var replies = current.Replies.ToList();
        var replyDebounce = new System.Windows.Forms.Timer { Interval = 600 };
        void SaveReplies() => Apply(current with
        {
            QuickReplies = replies.Select(reply => reply.Trim()).Where(reply => reply.Length > 0).ToArray(),
        });
        replyDebounce.Tick += (_, _) => { replyDebounce.Stop(); SaveReplies(); };
        var addReply = new FlatButton(ButtonKind.Secondary, "+ Add reply");
        void BuildReplies()
        {
            repliesList.SuspendLayout();
            while (repliesList.Controls.Count > 0) repliesList.Controls[0].Dispose();
            repliesList.RowStyles.Clear();
            for (int index = 0; index < replies.Count; index++)
            {
                int position = index;
                var field = new TextField("Reply text") { Width = 420, Margin = new Padding(0, 0, 8, 0) };
                field.Input.Text = replies[position];
                field.Input.MaxLength = 120;
                field.Input.TextChanged += (_, _) =>
                {
                    replies[position] = field.Input.Text;
                    replyDebounce.Stop();
                    replyDebounce.Start();
                };
                var remove = new FlatButton(ButtonKind.Ghost, "Remove") { AccessibleName = $"Remove reply {position + 1}" };
                // Rebuild after the click returns: disposing a button in its own Click is unsafe.
                remove.Click += (_, _) => repliesList.BeginInvoke(() =>
                {
                    replies.RemoveAt(position);
                    SaveReplies();
                    BuildReplies();
                });
                var row = Ui.Row(field, remove);
                row.Margin = new Padding(0, 0, 0, 6);
                repliesList.Add(row);
            }
            repliesList.ResumeLayout();
            addReply.Enabled = replies.Count < 8;
        }
        addReply.Click += (_, _) => repliesList.BeginInvoke(() =>
        {
            replies.Add("Hi, are you still interested?");
            SaveReplies();
            BuildReplies();
        });
        BuildReplies();
        var repliesLabel = Ui.Label("Quick replies", WindowTheme.Sans(9.5f), WindowTheme.Body);
        repliesLabel.Margin = new Padding(0, 14, 0, 2);
        var repliesHint = Ui.Label("Shown under Reply on each trade whisper. One press sends one message.",
            WindowTheme.Sans(8.5f), WindowTheme.Dim, wrap: true);
        repliesHint.Margin = new Padding(0, 0, 0, 8);
        var thanksLabel = Ui.Label("Thanks button", WindowTheme.Sans(9.5f), WindowTheme.Body);
        thanksLabel.Margin = new Padding(0, 0, 0, 6);
        stack.Add(Ui.Section("Trade replies",
            "Edit, add, or remove replies used in the desktop Trades tab and the in-game overlay.",
            thanksLabel, thanksField, repliesLabel, repliesHint, repliesList, addReply));

        // Accent colour.
        var swatches = Ui.Row();
        swatches.WrapContents = true;
        swatches.MaximumSize = new Size(700, 0);
        foreach (var theme in WindowTheme.Themes)
        {
            var chip = new FlatButton(ButtonKind.Chip, theme.Label)
            {
                Tag = theme.Id, Icon = WindowTheme.Swatch(theme.Accent, 16),
                Selected = theme.Id == WindowTheme.ThemeName, Margin = new Padding(0, 0, 6, 6),
            };
            chip.Click += (_, _) =>
            {
                Apply(current with { Theme = theme.Id });
                foreach (var other in swatches.Controls.OfType<FlatButton>()) other.Selected = Equals(other.Tag, theme.Id);
            };
            swatches.Controls.Add(chip);
        }
        stack.Add(Ui.Section("Accent colour",
            "Tints buttons, tabs and highlights in this window and the in-game overlay. The ExileWorth mark stays red.",
            swatches));

        // League.
        stack.Add(Ui.Section("Trade league",
            "Prices come from this league's public trade listings. Private leagues need a sign-in, which ExileWorth deliberately does not ask for.",
            leagueChips));
        leagueChips.WrapContents = true;
        leagueChips.MaximumSize = new Size(700, 0);

        // Overlay.
        var nearCursor = new Toggle { Checked = current.NearCursor, AccessibleName = "Open the overlay next to the cursor" };
        nearCursor.CheckedChanged += (_, _) => Apply(current with { NearCursor = nearCursor.Checked });
        stack.Add(Ui.Section("In-game overlay", null,
            SwitchRow(nearCursor, "Open next to the cursor", "Otherwise the panel opens in the top-right corner of the game's screen."),
            Ui.Label("The overlay never takes focus from the game, and hides itself when you switch to another app. " +
                "Drag its top bar to move it.", WindowTheme.Sans(8.5f), WindowTheme.Dim, wrap: true)));

        // Startup.
        var startup = new Toggle { Checked = StartsWithWindows(), AccessibleName = "Start with Windows" };
        startup.CheckedChanged += (_, _) =>
        {
            try
            {
                SetStartsWithWindows(startup.Checked);
                ShowFeedback(startup.Checked ? "ExileWorth will start in the tray when you sign in." : "Start with Windows turned off.");
            }
            catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                ShowFeedback("Windows did not allow changing the startup setting.", error: true);
            }
        };
        stack.Add(Ui.Section("Startup", null,
            SwitchRow(startup, "Start with Windows", "Opens quietly in the notification area, ready for the shortcut.")));

        var advancedCopy = new Toggle { Checked = current.AdvancedItemCopy == true, AccessibleName = "Use advanced item copy" };
        advancedCopy.CheckedChanged += (_, _) => Apply(current with { AdvancedItemCopy = advancedCopy.Checked });
        stack.Add(Ui.Section("Item copy",
            "Standard copy uses Ctrl + C. The default Ctrl + Shift + D shortcut keeps Alt out of the price check.",
            SwitchRow(advancedCopy, "Use advanced item copy",
                "Uses Ctrl + Alt + C for extra modifier details. This may pin the game’s tooltip if Pin Popups uses Alt.")));

        // Data.
        var folder = new FlatButton(ButtonKind.Secondary, "Open settings folder");
        folder.Click += (_, _) =>
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vaaluation");
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        };
        stack.Add(Ui.Section("Data",
            "Settings are stored on this PC only. ExileWorth reads the public trade API and your game's chat log; nothing is uploaded.",
            folder));
        var github = new FlatButton(ButtonKind.Secondary, "GitHub ↗");
        var issue = new FlatButton(ButtonKind.Ghost, "Report an issue ↗");
        github.Click += (_, _) => Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        issue.Click += (_, _) => Process.Start(new ProcessStartInfo($"{RepositoryUrl}/issues/new/choose") { UseShellExecute = true });
        stack.Add(Ui.Section("Help", $"ExileWorth {Program.Version} for Path of Exile on Windows.",
            Ui.Row(github, issue)));

        feedback.Margin = new Padding(2, 4, 0, 0);
        stack.Add(feedback);

        scroller.Controls.Add(stack);
        page.Add(scroller, SizeType.Percent, 100);
        return page;
    }

    private static Control SwitchRow(Toggle toggle, string title, string detail)
    {
        var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 6), Dock = DockStyle.Top };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toggle.Margin = new Padding(0, 2, 12, 0);
        row.Controls.Add(toggle, 0, 0);
        row.SetRowSpan(toggle, 2);
        var label = Ui.Label(title, WindowTheme.Sans(9.5f), WindowTheme.Body);
        label.Margin = Padding.Empty;
        label.Cursor = Cursors.Hand;
        label.Click += (_, _) => toggle.Checked = !toggle.Checked;
        row.Controls.Add(label, 1, 0);
        row.Controls.Add(Ui.Label(detail, WindowTheme.Sans(8.5f), WindowTheme.Dim, wrap: true), 1, 1);
        return row;
    }

    private void Apply(DesktopPreferences next, Action? then = null)
    {
        if (next == current) { then?.Invoke(); return; }
        try
        {
            save(next);
            bool shortcutChanged = next.Shortcut != current.Shortcut;
            current = next;
            ShowFeedback(shortcutChanged ? $"Saved. Price check: {next.Shortcut} while Path of Exile is in front." : "Saved.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowFeedback("Could not save your settings.", error: true);
        }
        then?.Invoke();
    }

    private void ShowFeedback(string text, bool error = false)
    {
        feedback.Text = text;
        feedback.ForeColor = error ? WindowTheme.Down : WindowTheme.UpText;
    }

    private static bool StartsWithWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("ExileWorth") is string;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException) { return false; }
    }

    private static void SetStartsWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue("ExileWorth", $"\"{Application.ExecutablePath}\" --tray");
        else key.DeleteValue("ExileWorth", throwOnMissingValue: false);
    }

    // ── League ───────────────────────────────────────────────────────────

    public void SetLeagues(League[] available, string? selected)
    {
        Available = available;
        // Rebuild only when the leagues change: a chip's own click lands here,
        // and disposing the button inside its Click handler is unsafe.
        var existing = leagueChips.Controls.OfType<FlatButton>().ToArray();
        if (!existing.Select(chip => (chip.Tag as League)?.Id).SequenceEqual(available.Select(entry => (string?)entry.Id)))
        {
            leagueChips.SuspendLayout();
            while (leagueChips.Controls.Count > 0) leagueChips.Controls[0].Dispose();
            foreach (var entry in available)
            {
                var chip = new FlatButton(ButtonKind.Chip, entry.Text) { Tag = entry, Margin = new Padding(0, 0, 6, 6) };
                chip.Click += async (_, _) => await selectLeague(entry);
                leagueChips.Controls.Add(chip);
            }
            leagueChips.ResumeLayout();
            existing = leagueChips.Controls.OfType<FlatButton>().ToArray();
        }
        foreach (var chip in existing) chip.Selected = (chip.Tag as League)?.Id == selected;

        if (selectedLeague == selected) return;
        selectedLeague = selected;
        leagueRevision++;
        lastRates = null;
        rates.SetRows([]);
        currencyLabel.Text = selected is null ? "No trade league selected." : $"{selected} · refresh to load rates.";
        if (Currency.Parent is not null && selected is not null) CurrencyShown();
    }

    public void Dispose()
    {
        Worker.Dispose();
        tradeWorker.Dispose();
        denominationMenu.Dispose();
        Settings.Dispose();
        Currency.Dispose();
        Trades.Dispose();
    }
}

/// <summary>Currency rows with their icons, priced in the chosen denomination.</summary>
internal sealed class CurrencyView : ScrollList
{
    private const int ThinSample = 8;
    private CurrencyRow[] rows = [];
    private CurrencyRow[] shown = [];
    private string denomination = "chaos";

    public CurrencyView()
    {
        AccessibleName = "Currency rates";
        AccessibleRole = AccessibleRole.Table;
        CurrencyIcons.Changed += Invalidate;
    }

    public string Denomination
    {
        get => denomination;
        set { denomination = value; Rebuild(); }
    }

    public void SetRows(CurrencyRow[] value)
    {
        rows = value;
        Rebuild();
    }

    /// <summary>Restates chaos rates in the denomination, adding Chaos itself when it is not the unit.</summary>
    private void Rebuild()
    {
        double divisor = 1;
        if (denomination != "chaos")
        {
            var unit = rows.FirstOrDefault(row => row.Id == denomination)?.Rate;
            divisor = unit is { Median: > 0 } ? unit.Median : double.NaN;
        }
        var list = new List<CurrencyRow>();
        if (denomination != "chaos" && rows.Length > 0)
            list.Add(new CurrencyRow("chaos", "Chaos Orb", double.IsFinite(divisor) ? new CurrencyQuote(1 / divisor, int.MaxValue) : null, null));
        foreach (var row in rows.Where(row => row.Id != denomination))
            list.Add(row with
            {
                Rate = row.Rate is { } rate && double.IsFinite(divisor) ? rate with { Median = rate.Median / divisor } : null,
            });
        shown = [.. list];
        Reset();
    }

    protected override int Count => shown.Length;
    protected override int RowHeight => this.Px(40);
    protected override int HeaderHeight => this.Px(26);

    protected override void DrawHeader(Graphics g, Rectangle bounds)
    {
        var font = WindowTheme.Mono(7f);
        WindowTheme.Text(g, "CURRENCY", font, new Rectangle(this.Px(46), 0, Width / 2, bounds.Height), WindowTheme.Faint);
        WindowTheme.Text(g, "RATE", font, new Rectangle(0, 0, Width - this.Px(20), bounds.Height), WindowTheme.Faint,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        using var line = new Pen(WindowTheme.Hairline);
        g.DrawLine(line, 0, bounds.Bottom - 1, Width - this.Px(8), bounds.Bottom - 1);
    }

    protected override void DrawRow(Graphics g, int index, Rectangle bounds, bool hovered)
    {
        var row = shown[index];
        if (hovered) WindowTheme.FillRounded(g, Rectangle.Inflate(bounds, 0, -2), this.Px(6), WindowTheme.Hover);
        int icon = this.Px(26);
        var iconBox = new Rectangle(bounds.Left + this.Px(8), bounds.Top + (bounds.Height - icon) / 2, icon, icon);
        if (CurrencyIcons.Get(row.Id) is { } image)
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, iconBox);
        }
        else WindowTheme.FillRounded(g, iconBox, this.Px(6), WindowTheme.Chip);
        var rateBox = Rectangle.FromLTRB(bounds.Right - this.Px(220), bounds.Top, bounds.Right - this.Px(12), bounds.Bottom);
        WindowTheme.Text(g, row.Label, WindowTheme.Sans(10f), Rectangle.FromLTRB(iconBox.Right + this.Px(12), bounds.Top, rateBox.Left, bounds.Bottom),
            WindowTheme.Body);

        if (row.Rate is { } rate)
        {
            string amount = Format(rate.Median);
            var big = WindowTheme.Mono(10.5f, FontStyle.Bold);
            var unitFont = WindowTheme.Sans(8.5f);
            string unit = denomination;
            int unitWidth = TextRenderer.MeasureText(g, unit, unitFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            bool thin = rate.SampleSize < ThinSample;
            int middle = bounds.Top + bounds.Height / 2;
            var line = thin ? new Rectangle(rateBox.Left, bounds.Top + this.Px(3), rateBox.Width, middle - bounds.Top) : rateBox;
            WindowTheme.Text(g, unit, unitFont, line, WindowTheme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            WindowTheme.Text(g, amount, big, Rectangle.FromLTRB(line.Left, line.Top, line.Right - unitWidth - this.Px(6), line.Bottom),
                WindowTheme.Foreground, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            if (thin)
                WindowTheme.Text(g, $"from {rate.SampleSize} offers", WindowTheme.Mono(7f),
                    new Rectangle(rateBox.Left, middle, rateBox.Width, bounds.Bottom - middle - this.Px(3)),
                    WindowTheme.Down, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
        else
        {
            WindowTheme.Text(g, row.Error ?? "no offers", WindowTheme.Sans(8.5f), rateBox,
                row.Error is null ? WindowTheme.Faint : WindowTheme.Down, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
        if (index < shown.Length - 1)
        {
            using var pen = new Pen(WindowTheme.Hairline);
            g.DrawLine(pen, iconBox.Right + this.Px(12), bounds.Bottom - 1, bounds.Right - this.Px(8), bounds.Bottom - 1);
        }
    }

    private static string Format(double value) =>
        value >= 1000 ? Math.Round(value).ToString("#,0")
        : value >= 10 ? value.ToString("0")
        : value >= 1 ? value.ToString("0.0")
        : value >= 0.01 ? value.ToString("0.00")
        : value.ToString("G2");

    protected override void Dispose(bool disposing)
    {
        if (disposing) CurrencyIcons.Changed -= Invalidate;
        base.Dispose(disposing);
    }
}
