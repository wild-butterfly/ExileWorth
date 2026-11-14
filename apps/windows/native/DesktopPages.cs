namespace ExileWorth.Windows;

internal sealed class DesktopPages : IDisposable
{
    private readonly Worker worker = new();
    private readonly ComboBox leagues = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly Label currencyStatus = new() { AutoSize = true, Text = "Refresh to load rates for the selected league." };
    private readonly DataGridView rates = new() { Dock = DockStyle.Fill };
    private bool updatingLeagues;
    private string? selectedLeague;
    private int leagueRevision;

    public Control Settings { get; }
    public Control Currency { get; }
    public League[] Available { get; private set; } = [];

    public DesktopPages(DesktopPreferences preferences, Action<DesktopPreferences> save,
        Func<string?> preferred, Func<League, Task> selectLeague)
    {
        var modifier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
        modifier.Items.AddRange(Shortcut.ModifierOptions.Select(Shortcut.FormatModifiers).ToArray());
        modifier.SelectedIndex = Array.IndexOf(Shortcut.ModifierOptions, preferences.Shortcut.Modifiers);
        var key = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
        key.Items.AddRange(Shortcut.KeyOptions.Cast<object>().ToArray());
        key.SelectedItem = preferences.Shortcut.Key;
        var placement = new CheckBox { Text = "Open near the cursor", AutoSize = true, Checked = preferences.NearCursor };
        var feedback = new Label { AutoSize = true, ForeColor = WindowTheme.Muted };
        var apply = new GlassButton(true) { Text = "Save settings", AutoSize = true };
        var reset = new GlassButton { Text = "Reset shortcut", AutoSize = true };
        var shortcut = new FlowLayoutPanel { AutoSize = true };
        shortcut.Controls.AddRange([modifier, key]);
        var actions = new FlowLayoutPanel { AutoSize = true };
        actions.Controls.AddRange([apply, reset]);
        var settingsLayout = Page("Settings");
        Add(settingsLayout, new Label { Text = "Price-check shortcut", AutoSize = true });
        Add(settingsLayout, shortcut);
        Add(settingsLayout, new Label { Text = "Trade league", AutoSize = true });
        Add(settingsLayout, leagues);
        Add(settingsLayout, new Label { Text = "Overlay placement", AutoSize = true });
        Add(settingsLayout, placement);
        Add(settingsLayout, actions);
        Add(settingsLayout, feedback);
        Settings = settingsLayout;

        leagues.SelectedIndexChanged += async (_, _) =>
        {
            if (!updatingLeagues && leagues.SelectedItem is League entry) await selectLeague(entry);
        };
        reset.Click += (_, _) =>
        {
            modifier.SelectedIndex = Array.IndexOf(Shortcut.ModifierOptions, Shortcut.Default.Modifiers);
            key.SelectedItem = Shortcut.Default.Key;
            feedback.Text = "Select Save settings to apply.";
        };
        apply.Click += (_, _) =>
        {
            try
            {
                var value = new Shortcut(Shortcut.ModifierOptions[modifier.SelectedIndex], (Keys)key.SelectedItem!);
                save(new DesktopPreferences(value, placement.Checked));
                feedback.Text = $"Saved. Press {value} while Path of Exile is in focus.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                feedback.Text = "Could not save your settings.";
            }
        };

        WindowTheme.StyleListings(rates);
        rates.Columns.Clear();
        rates.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Currency", FillWeight = 55 });
        rates.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Per 1 Divine Orb", FillWeight = 28 });
        rates.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Offers", FillWeight = 17 });
        var refresh = new GlassButton(true) { Text = "Refresh rates", AutoSize = true };
        var currencyLayout = Page("Currency");
        Add(currencyLayout, currencyStatus);
        Add(currencyLayout, refresh);
        currencyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        currencyLayout.Controls.Add(rates);
        Currency = currencyLayout;

        refresh.Click += async (_, _) =>
        {
            refresh.Enabled = false;
            rates.Rows.Clear();
            currencyStatus.Text = "Loading bulk exchange rates…";
            int requested = leagueRevision;
            try
            {
                var result = await worker.SendAsync<CurrencyResult>(new { action = "currency", preferred = preferred() },
                    TimeSpan.FromMinutes(3));
                if (Currency.IsDisposed || leagueRevision != requested) return;
                double? divine = result.Rows.FirstOrDefault(row => row.Id == "divine")?.Rate?.Median;
                bool hasDivineRate = divine is > 0 && double.IsFinite(divine.Value);
                rates.Rows.Add("Chaos Orb", hasDivineRate ? FormatRate(divine!.Value) : "Unavailable", "—");
                foreach (var row in result.Rows.Where(row => row.Id != "divine"))
                {
                    int index = rates.Rows.Add(row.Label,
                        hasDivineRate && row.Rate is { Median: > 0 } quote && double.IsFinite(quote.Median)
                            ? FormatRate(divine!.Value / quote.Median)
                            : row.Rate is null && row.Error is null ? "No offers" : "Unavailable",
                        row.Rate?.SampleSize.ToString() ?? "—");
                    if (row.Error is not null) rates.Rows[index].Cells[1].ToolTipText = row.Error;
                }
                rates.ClearSelection();
                currencyStatus.Text = hasDivineRate
                    ? $"{result.Selected} · Estimated from bulk asking rates · {DateTime.Now:t}"
                    : $"{result.Selected} · Divine Orb rate unavailable. Try refreshing.";
            }
            catch (Exception error)
            {
                if (!Currency.IsDisposed && leagueRevision == requested) currencyStatus.Text = error.Message;
            }
            finally
            {
                if (!Currency.IsDisposed) refresh.Enabled = true;
            }
        };
    }

    private static string FormatRate(double amount) =>
        amount >= 0.01 ? amount.ToString("#,0.##") : amount.ToString("G3");

    public void SetLeagues(League[] available, string? selected)
    {
        Available = available;
        updatingLeagues = true;
        try
        {
            leagues.Items.Clear();
            leagues.Items.AddRange(available);
            leagues.SelectedIndex = Array.FindIndex(available, entry => entry.Id == selected);
        }
        finally { updatingLeagues = false; }

        if (selectedLeague == selected) return;
        selectedLeague = selected;
        leagueRevision++;
        rates.Rows.Clear();
        currencyStatus.Text = selected is null ? "No trade league selected." : $"{selected} · Refresh to load rates.";
    }

    private static TableLayoutPanel Page(string title)
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16), AutoScroll = true };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var heading = new Label { Text = title, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 18, FontStyle.Bold) };
        Add(page, heading);
        return page;
    }

    private static void Add(TableLayoutPanel page, Control control)
    {
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.Controls.Add(control);
    }

    public void Dispose()
    {
        worker.Dispose();
        Settings.Dispose();
        Currency.Dispose();
    }
}
