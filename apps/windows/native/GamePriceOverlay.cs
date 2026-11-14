using System.Runtime.InteropServices;

namespace ExileWorth.Windows;

internal sealed class GamePriceOverlay : Form
{
    private readonly Label title = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly DataGridView prices = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill };
    private nint game;
    private readonly Label tradeStatus = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = WindowTheme.Muted };
    private string itemName = "Price Check";
    private readonly Worker tradeWorker = new();
    private readonly ListBox trades = new()
    {
        Dock = DockStyle.Fill, BackColor = WindowTheme.Surface, ForeColor = WindowTheme.Foreground,
        BorderStyle = BorderStyle.None,
    };
    private readonly FlowLayoutPanel tradeActions = new() { Dock = DockStyle.Fill };
    private bool showingTrades;
    private bool loadingTrades;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return parameters;
        }
    }

    public GamePriceOverlay(Action<string?> selectCurrency)
    {
        Text = "ExileWorth Price Overlay";
        AccessibleName = Text;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = WindowTheme.Background;
        ForeColor = WindowTheme.Foreground;
        AutoScaleMode = AutoScaleMode.Dpi;
        title.Font = new Font(Font.FontFamily, 13, FontStyle.Bold);
        status.ForeColor = WindowTheme.Muted;

        WindowTheme.StyleListings(prices);
        prices.Columns.RemoveAt(3);
        prices.Columns.RemoveAt(1);
        prices.TabStop = false;
        foreach (var (label, currency) in new (string, string?)[]
        {
            ("All", null), ("Alchemy", "alch"), ("Chaos", "chaos"), ("Divine", "divine"),
        })
        {
            var button = new GlassButton { Text = label, AutoSize = true, Tag = currency };
            button.Click += (_, _) => selectCurrency(currency);
            actions.Controls.Add(button);
        }
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var priceTab = new GlassButton { Text = "Price", AutoSize = true };
        var tradeTab = new GlassButton { Text = "Trades", AutoSize = true };
        var dismiss = new GlassButton { Text = "×", AccessibleName = "Hide overlay", AutoSize = true };
        dismiss.Click += (_, _) => Hide();
        tabs.Controls.AddRange([priceTab, tradeTab, dismiss]);
        var reload = new GlassButton { Text = "Refresh", AutoSize = true };
        var copy = new GlassButton { Text = "Copy reply", AutoSize = true, Enabled = false };
        tradeActions.Controls.AddRange([reload, copy]);
        priceTab.Click += (_, _) => ShowTrades(false);
        tradeTab.Click += async (_, _) =>
        {
            ShowTrades(true);
            await LoadTradesAsync();
        };
        reload.Click += async (_, _) => await LoadTradesAsync();
        trades.SelectedIndexChanged += (_, _) => copy.Enabled = trades.SelectedItem is TradeWhisper;
        copy.Click += (_, _) =>
        {
            if (trades.SelectedItem is not TradeWhisper trade) return;
            try
            {
                Clipboard.SetText(trade.Reply);
                tradeStatus.Text = "Reply copied. Paste it into game chat.";
            }
            catch (ExternalException) { tradeStatus.Text = "Clipboard busy. Try copying again."; }
        };

        var card = new GlassPanel(accent: true) { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(tabs, 0, 0);
        layout.Controls.Add(title, 0, 1);
        layout.Controls.Add(Host(status, tradeStatus), 0, 2);
        layout.Controls.Add(Host(prices, trades), 0, 3);
        layout.Controls.Add(Host(actions, tradeActions), 0, 4);
        ShowTrades(false);
        card.Controls.Add(layout);
        Controls.Add(card);
        ClientSize = new Size(435, 270);
    }

    public void Open(nint target, bool nearCursor)
    {
        game = target;
        ShowTrades(false);
        if (!Visible)
        {
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
    }

    public void Present(string name, string message, IReadOnlyList<Listing> listings, string? currency, bool busy)
    {
        itemName = name;
        if (!showingTrades) title.Text = name;
        status.Text = message;
        prices.Rows.Clear();
        foreach (var listing in listings)
            prices.Rows.Add($"{listing.Amount:g} {listing.Currency}", listing.AccountName);
        prices.ClearSelection();
        foreach (var button in actions.Controls.OfType<Button>())
        {
            bool selected = Equals(button.Tag, currency);
            button.Enabled = !busy && !selected;
            button.AccessibleDescription = selected ? "Selected currency" : null;
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

    private static Panel Host(Control first, Control second)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        panel.Controls.AddRange([first, second]);
        return panel;
    }

    private void ShowTrades(bool show)
    {
        showingTrades = show;
        title.Text = show ? "Trade requests" : itemName;
        status.Visible = prices.Visible = actions.Visible = !show;
        tradeStatus.Visible = trades.Visible = tradeActions.Visible = show;
    }

    private async Task LoadTradesAsync()
    {
        if (loadingTrades) return;
        loadingTrades = true;
        tradeStatus.Text = "Reading recent trade whispers…";
        trades.Items.Clear();
        try
        {
            string? path = GameHotkey.ClientLog(game);
            if (path is null) throw new IOException("Could not find Path of Exile’s Client.txt.");
            string[] lines;
            await using (var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true))
            {
                long start = Math.Max(0, file.Length - 65536);
                file.Position = start;
                using var reader = new StreamReader(file);
                if (start > 0) await reader.ReadLineAsync();
                lines = (await reader.ReadToEndAsync()).Split('\n');
            }
            var result = await tradeWorker.SendAsync<TradeWhisper[]>(new { action = "trades", lines });
            if (IsDisposed) return;
            trades.Items.AddRange(result);
            tradeStatus.Text = result.Length == 0 ? "No recent trade whispers." : "Select a whisper to copy a reply.";
        }
        catch (Exception error)
        {
            if (!IsDisposed) tradeStatus.Text = error.Message;
        }
        finally { loadingTrades = false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tradeWorker.Dispose();
        base.Dispose(disposing);
    }
}
