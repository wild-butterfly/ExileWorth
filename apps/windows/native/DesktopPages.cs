using System.Diagnostics;
using Microsoft.Win32;

namespace ExileWorth.Windows;

internal sealed class DesktopPages : IDisposable
{
    private const string RepositoryUrl = "https://github.com/wild-butterfly/ExileWorth";

    private readonly Worker tradeWorker = new();
    private readonly Func<string?> preferred;
    private readonly Func<DesktopPreferences> settings;
    private readonly Func<League, Task> selectLeague;
    private readonly Action<DesktopPreferences> save;
    private DesktopPreferences current;

    // Currency: every exchange item, priced from completed trades.
    private readonly MarketPage market;

    // Trades.
    private readonly TradeView trades = new(compact: false) { Dock = DockStyle.Fill };
    private readonly Label tradeStatus = Ui.Label("", WindowTheme.Sans(9f), WindowTheme.Dim);
    private bool loadingTrades;

    // Settings.
    private readonly FlowLayoutPanel leagueChips = Ui.Row();
    private readonly Label feedback = Ui.Label("", WindowTheme.Sans(9f), WindowTheme.UpText);
    private string? selectedLeague;

    private readonly Toggle startup = new() { AccessibleName = "Start with Windows" };
    private bool syncingStartup;

    /// <summary>Reflects a change made elsewhere (the first-run banner) without re-saving.</summary>
    public void SyncStartup(DesktopPreferences updated)
    {
        current = updated;
        syncingStartup = true;
        try { startup.Checked = Startup.IsEnabled(); }
        finally { syncingStartup = false; }
    }

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
        Currency = market = new MarketPage(() => selectedLeague ?? preferred());
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

    public void CurrencyShown() => market.Shown();

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
        var easyShortcut = new Shortcut(1, Keys.D);
        var easy = new FlatButton(ButtonKind.Secondary, "Use Alt + D");
        void ShowShortcut()
        {
            modifier.Text = $"{Shortcut.FormatModifiers(current.Shortcut.Modifiers)}  ▾";
            key.Text = $"{current.Shortcut.Key}  ▾";
            foreach (ToolStripMenuItem item in modifierMenu.Items)
            {
                item.Checked = Equals(item.Tag, current.Shortcut.Modifiers);
                item.Enabled = Shortcut.IsAllowed((uint)item.Tag!, current.Shortcut.Key);
            }
            foreach (ToolStripMenuItem item in keyMenu.Items)
            {
                item.Checked = Equals(item.Tag, current.Shortcut.Key);
                item.Enabled = Shortcut.IsAllowed(current.Shortcut.Modifiers, (Keys)item.Tag!);
            }
            reset.Enabled = current.Shortcut != Shortcut.Default;
            easy.Visible = current.Shortcut != easyShortcut;
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
        easy.Click += (_, _) => Apply(current with { Shortcut = easyShortcut }, ShowShortcut);
        ShowShortcut();
        var plus = Ui.Label("+", WindowTheme.Sans(10f), WindowTheme.Faint);
        plus.Anchor = AnchorStyles.None;
        plus.Margin = new Padding(0, 0, 8, 0);
        stack.Add(Ui.Section("Price-check shortcut",
            "Press it while hovering an item in Path of Exile. It only works while the game is in front, " +
            "and ExileWorth copies the item once. Alt + D is the easiest to press; if the game pins its item tooltip " +
            "when you use it, move Path of Exile's Pin Popups binding off Alt or choose Ctrl + Shift.",
            Ui.Row(modifier, plus, key, easy, reset)));

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
        startup.Checked = Startup.IsEnabled();
        startup.CheckedChanged += (_, _) =>
        {
            if (syncingStartup) return;
            try
            {
                Startup.SetEnabled(startup.Checked);
                Apply(current with { StartupPromptAnswered = true });
                ShowFeedback(startup.Checked ? "ExileWorth will start in the tray when you sign in." : "Start with Windows turned off.");
            }
            catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                ShowFeedback("Windows did not allow changing the startup setting.", error: true);
            }
        };
        stack.Add(Ui.Section("Startup", null,
            SwitchRow(startup, "Start with Windows", "Opens quietly in the notification area when you sign in, so the shortcut is ready whenever Path of Exile is in front.")));

        var advancedCopy = new Toggle { Checked = current.AdvancedItemCopy == true, AccessibleName = "Use advanced item copy" };
        advancedCopy.CheckedChanged += (_, _) => Apply(current with { AdvancedItemCopy = advancedCopy.Checked });
        stack.Add(Ui.Section("Item copy",
            "Standard copy uses Ctrl + C. The default shortcut is Alt + D; if the game pins its tooltip, change its Pin Popups binding or choose another shortcut.",
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
        market.LeagueChanged();
    }

    public void Dispose()
    {
        Worker.Dispose();
        tradeWorker.Dispose();
        Settings.Dispose();
        Currency.Dispose();
        Trades.Dispose();
    }
}
