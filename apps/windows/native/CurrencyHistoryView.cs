using System.Drawing.Drawing2D;

namespace ExileWorth.Windows;

internal record HistoryCurrency(string Id, string Label)
{
    public override string ToString() => Label;
}
internal record HistoryPoint(long At, double? Rate, double? GiveVolume, double? WantVolume);
internal record HistoryResult(HistoryPoint[] Points, int FailedHours);

/// <summary>Desktop exchange history using the same public digests and currency catalogue as Mac.</summary>
internal sealed class CurrencyHistoryView : UserControl
{
    private readonly Worker worker = new();
    private readonly ComboBox give = Picker("From currency"), want = Picker("To currency");
    private readonly Label headline = Ui.Label("", WindowTheme.Sans(12f), WindowTheme.Foreground);
    private readonly Label source = Ui.Label("GGG completed trades · volume-weighted hourly average · sampled daily", WindowTheme.Sans(8f), WindowTheme.Dim);
    private readonly HistoryPlot plot = new() { Dock = DockStyle.Fill };
    private readonly List<(FlatButton Button, string Period)> periods = [];
    private string period = "7d";
    private string? league;
    private int revision;
    private bool loading, initialising;

    private static ComboBox Picker(string name) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend,
        AutoCompleteSource = AutoCompleteSource.ListItems, FlatStyle = FlatStyle.Flat,
        AccessibleName = name, Font = WindowTheme.Sans(9f), Width = 150, DropDownWidth = 220,
        BackColor = WindowTheme.Sunken, ForeColor = WindowTheme.Foreground,
    };

    public CurrencyHistoryView()
    {
        Dock = DockStyle.Fill;
        BackColor = WindowTheme.Card;
        headline.AutoSize = false;
        headline.Dock = DockStyle.Fill;
        headline.AutoEllipsis = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, this.Px(34)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, this.Px(30)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, this.Px(24)));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        give.Width = want.Width = this.Px(142);
        toolbar.Controls.Add(give);
        toolbar.Controls.Add(Ui.Label("→", WindowTheme.Sans(10f), WindowTheme.Muted));
        toolbar.Controls.Add(want);
        foreach (string value in new[] { "24h", "7d", "30d" })
        {
            var button = new FlatButton(ButtonKind.Chip, value) { Selected = value == period };
            button.Click += (_, _) =>
            {
                period = value;
                foreach (var entry in periods) entry.Button.Selected = entry.Period == period;
                RefreshHistory();
            };
            periods.Add((button, value));
            toolbar.Controls.Add(button);
        }
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(headline, 0, 1);
        layout.Controls.Add(plot, 0, 2);
        source.Dock = DockStyle.Fill;
        source.AutoEllipsis = true;
        layout.Controls.Add(source, 0, 3);
        Controls.Add(layout);
        give.SelectedIndexChanged += (_, _) => { if (!initialising) RefreshHistory(); };
        want.SelectedIndexChanged += (_, _) => { if (!initialising) RefreshHistory(); };
        plot.Placeholder = "Select a league to view currency history.";
    }

    public void SetLeague(string? value)
    {
        if (league == value) return;
        league = value;
        RefreshHistory();
    }

    public void RefreshHistory()
    {
        revision++;
        if (!loading && !IsDisposed && league is not null) _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        loading = true;
        int requested = revision;
        try
        {
            if (give.Items.Count == 0)
            {
                var currencies = await worker.SendAsync<HistoryCurrency[]>(new { action = "historyCurrencies" });
                if (IsDisposed) return;
                initialising = true;
                give.Items.AddRange(currencies);
                want.Items.AddRange(currencies);
                give.SelectedItem = currencies.First(currency => currency.Id == "divine");
                want.SelectedItem = currencies.First(currency => currency.Id == "chaos");
                initialising = false;
            }
            do
            {
                requested = revision;
                if (give.SelectedItem is not HistoryCurrency from || want.SelectedItem is not HistoryCurrency to) break;
                plot.Points = [];
                plot.Placeholder = "Loading completed-hour exchange history…";
                headline.Text = "";
                string selectedPeriod = period;
                var result = await worker.SendAsync<HistoryResult>(new { action = "history", preferred = league, give = from.Id, want = to.Id, period = selectedPeriod }, TimeSpan.FromMinutes(3));
                if (IsDisposed) return;
                if (requested != revision) continue;
                plot.Unit = $"{to.Label} / {from.Label}";
                plot.Points = result.Points;
                plot.Placeholder = result.FailedHours > 0 ? "History unavailable. Refresh to retry." : "No completed trades for this pair in the sampled hours.";
                var valid = result.Points.Where(point => point.Rate is not null).ToArray();
                if (valid.LastOrDefault()?.Rate is double last)
                {
                    string change = valid.Length < 2 ? "" : $" · {(last >= valid[0].Rate!.Value ? "↑" : "↓")} {Math.Abs((last / valid[0].Rate!.Value - 1) * 100):0.0}% between available samples";
                    headline.Text = $"{last:0.#####} {to.Id} / {from.Label}{change}";
                }
                source.Text = "GGG completed trades · hourly average" + (selectedPeriod == "24h" ? "" : " · sampled daily") + (result.FailedHours > 0 ? " · some history unavailable" : " · gaps mean no recorded data");
            } while (requested != revision && !IsDisposed);
        }
        catch (Exception error)
        {
            if (!IsDisposed) { plot.Placeholder = error.Message; plot.Points = []; }
        }
        finally
        {
            loading = false;
            if (!IsDisposed && requested != revision) _ = LoadAsync();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) worker.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class HistoryPlot : Control
{
    private HistoryPoint[] points = [];
    private readonly ToolTip tip = new();
    public string Unit { get; set; } = "";
    public string Placeholder { get; set; } = "";
    public HistoryPoint[] Points { get => points; set { points = value; Invalidate(); } }
    private int LeftPad => this.Px(62);
    private int RightPad => this.Px(14);
    private float X(long at) => LeftPad + (at - points[0].At) / (float)Math.Max(1, points[^1].At - points[0].At) * Math.Max(1, Width - LeftPad - RightPad);

    public HistoryPlot()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = WindowTheme.Card;
        AccessibleName = "Currency price history";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var valid = points.Where(point => point.Rate is not null).ToArray();
        if (valid.Length == 0)
        {
            WindowTheme.Text(e.Graphics, Placeholder, WindowTheme.Sans(9f), ClientRectangle, WindowTheme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        float low = (float)valid.Min(point => point.Rate!.Value), high = (float)valid.Max(point => point.Rate!.Value);
        float pad = Math.Max((high - low) * .12f, Math.Max(high * .01f, .000001f));
        float min = Math.Max(0, low - pad), max = high + pad;
        int top = this.Px(10), bottom = Height - this.Px(24);
        float Y(double value) => top + (max - (float)value) / (max - min) * Math.Max(1, bottom - top);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var grid = new Pen(WindowTheme.Hairline);
        using var line = new Pen(WindowTheme.AccentText, 2f * DeviceDpi / 96);
        using var dot = new SolidBrush(WindowTheme.AccentText);
        for (int tick = 0; tick < 3; tick++)
        {
            float value = min + (max - min) * tick / 2;
            int yy = (int)Y(value);
            g.DrawLine(grid, LeftPad, yy, Width - RightPad, yy);
            WindowTheme.Text(g, value.ToString("G4"), WindowTheme.Mono(7.5f), new Rectangle(0, yy - this.Px(8), LeftPad - this.Px(8), this.Px(16)), WindowTheme.Dim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
        PointF? previous = null;
        foreach (var point in points)
        {
            if (point.Rate is not double rate) { previous = null; continue; }
            var current = new PointF(X(point.At), Y(rate));
            if (previous is PointF before) g.DrawLine(line, before, current);
            g.FillEllipse(dot, current.X - 2, current.Y - 2, 4, 4);
            previous = current;
        }
        string Date(long at) => DateTimeOffset.FromUnixTimeMilliseconds(at).LocalDateTime.ToString("dd MMM HH:mm");
        var labels = Rectangle.FromLTRB(LeftPad, bottom + this.Px(5), Width - RightPad, Height);
        WindowTheme.Text(g, Date(points[0].At), WindowTheme.Sans(7.5f), labels, WindowTheme.Dim);
        WindowTheme.Text(g, Date(points[^1].At), WindowTheme.Sans(7.5f), labels, WindowTheme.Dim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (points.Length == 0) return;
        var point = points.MinBy(point => Math.Abs(X(point.At) - e.X))!;
        string value = point.Rate is double rate ? $"{rate:0.#####} {Unit}\n{point.GiveVolume:0.##} source / {point.WantVolume:0.##} quote units traded" : "No trades recorded";
        string text = $"{DateTimeOffset.FromUnixTimeMilliseconds(point.At).LocalDateTime:g}\n{value}";
        if (tip.GetToolTip(this) != text) tip.SetToolTip(this, text);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tip.Dispose();
        base.Dispose(disposing);
    }
}
