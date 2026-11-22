using System.Drawing.Drawing2D;

namespace ExileWorth.Windows;

internal record MarketRowData(string Id, string Name, string Category, string? Icon, double Chaos,
    double Volume, double?[] Series, double? Change);
internal record MarketResult(string League, string Period, long LatestHour, int FailedHours,
    string? ChaosIcon, MarketRowData[] Rows);
internal record ItemPoint(long At, double? Value, double? Volume);
internal record ItemHistoryResult(ItemPoint[] Points, int FailedHours);

/// <summary>
/// Every item on GGG's Currency Exchange, priced from completed trades in the
/// last 24 hours, shown in any currency the player chooses.
/// </summary>
internal sealed class MarketPage : UserControl
{
    public const string ChaosId = "Metadata/Items/Currency/CurrencyRerollRare";
    public const string DivineId = "Metadata/Items/Currency/CurrencyModValues";
    private const string ExaltedId = "Metadata/Items/Currency/CurrencyAddModToRare";
    private const string MirrorId = "Metadata/Items/Currency/CurrencyDuplicate";

    public static readonly (string Id, string Label)[] Categories =
    [
        ("All", "All items"), ("Currency", "Currency"), ("Fragments", "Fragments"), ("Essences", "Essences"),
        ("Delve", "Fossils & Resonators"), ("Oils", "Oils"), ("Catalysts", "Catalysts"),
        ("DeliriumOrbs", "Delirium Orbs"), ("Ancestor", "Tattoos & Omens"), ("Runegrafts", "Runegrafts"),
        ("Keepers", "Foulborn & Wombgifts"), ("Expedition", "Expedition"), ("EnshroudingCrystals", "Crystals"),
        ("Sanctum", "Sanctum"), ("Heist", "Heist"), ("Beasts", "Beasts"), ("Cards", "Divination Cards"),
        ("MapKey", "Maps"), ("Other", "Other"),
    ];

    private readonly Worker worker = new();
    private readonly Func<string?> preferred;
    private readonly Label meta = Ui.Label("Loading completed trades…", WindowTheme.Sans(9f), WindowTheme.Muted);
    private readonly FlatButton unitButton = new(ButtonKind.Secondary, "Auto  ▾") { AccessibleName = "Show prices in" };
    private readonly ContextMenuStrip unitMenu = WindowTheme.Menu();
    private readonly FlatButton refresh = new(ButtonKind.Primary, "Refresh");
    private readonly List<(FlatButton Button, string Period)> periodButtons = [];
    private readonly CategoryList categories = new() { Dock = DockStyle.Fill };
    private readonly TextField search = new("Search items…") { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 10) };
    private readonly MarketTable table = new() { Dock = DockStyle.Fill };
    private readonly MarketDetail detail;
    private readonly Panel body = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Control listView;
    private MarketResult? data;
    private string period = "24h";
    private string unit = "auto";
    private string category = "All";
    private string? loadedLeague;
    private int revision;
    private bool loading;

    public MarketPage(Func<string?> preferred)
    {
        this.preferred = preferred;
        Dock = DockStyle.Fill;
        BackColor = WindowTheme.Background;
        Padding = new Padding(this.Px(24), this.Px(20), this.Px(24), this.Px(20));
        int S(int value) => this.Px(value);
        detail = new MarketDetail(worker, preferred) { Dock = DockStyle.Fill };
        detail.Back += () => ShowList();

        // Header: title and freshness on the left, unit, period and refresh on the right.
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titles = Ui.Stack();
        titles.Add(Ui.Label("Currency", WindowTheme.Display(18f), WindowTheme.Foreground)).Margin = Padding.Empty;
        meta.AutoEllipsis = true;
        titles.Add(meta);
        header.Controls.Add(titles, 0, 0);
        var actions = Ui.Row();
        actions.Anchor = AnchorStyles.Right;
        actions.Controls.Add(Ui.Label("Price in", WindowTheme.Sans(8.5f), WindowTheme.Dim));
        actions.Controls[0].Margin = new Padding(0, S(9), S(6), 0);
        actions.Controls.Add(unitButton);
        foreach (string value in new[] { "24h", "7d" })
        {
            var button = new FlatButton(ButtonKind.Chip, value) { Selected = value == period, Margin = new Padding(0, S(5), S(4), 0) };
            new ToolTip().SetToolTip(button, value == "24h" ? "Sparklines show each of the last 24 hours" : "Sparklines show each of the last 7 days");
            button.Click += (_, _) =>
            {
                if (period == value) return;
                period = value;
                foreach (var (other, id) in periodButtons) other.Selected = id == period;
                _ = LoadAsync();
            };
            periodButtons.Add((button, value));
            actions.Controls.Add(button);
        }
        refresh.Margin = new Padding(S(8), 0, 0, 0);
        actions.Controls.Add(refresh);
        header.Controls.Add(actions, 1, 0);

        unitButton.Click += (_, _) => unitMenu.Show(unitButton, new Point(0, unitButton.Height + 4));
        refresh.Click += (_, _) => _ = LoadAsync();

        // List: categories on the left; search and the table on the right.
        var list = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, S(14), 0, 0) };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(190)));
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        list.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var categoryCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(S(8)), Margin = new Padding(0, 0, S(12), 0) };
        categoryCard.Controls.Add(categories);
        var tableCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(S(14), S(12), S(8), S(8)), Margin = Padding.Empty };
        tableCard.Controls.Add(table);
        tableCard.Controls.Add(search);
        list.Controls.Add(categoryCard, 0, 0);
        list.Controls.Add(tableCard, 1, 0);
        listView = list;

        body.Controls.Add(list);
        var note = Ui.Label("Prices are GGG's completed Currency Exchange trades over the last 24 hours, volume-weighted. " +
            "Item names: RePoE game data. Thinly traded items move more.", WindowTheme.Sans(8f), WindowTheme.Faint, wrap: true);
        note.Dock = DockStyle.Bottom;
        note.MaximumSize = new Size(S(900), 0);
        note.Margin = new Padding(0, S(8), 0, 0);
        Controls.Add(body);
        Controls.Add(note);
        Controls.Add(header);

        categories.Chosen += id =>
        {
            category = id;
            ApplyFilter();
        };
        search.Input.TextChanged += (_, _) => ApplyFilter();
        table.Opened += row => ShowDetail(row);
        table.SortChanged += ApplyFilter;
        CurrencyIcons.Changed += table.Invalidate;
        CurrencyIcons.Changed += categories.Invalidate;
        BuildUnitMenu();
    }

    /// <summary>Loads the first time the page is shown, and again when the league changes.</summary>
    public void Shown()
    {
        string? league = preferred();
        if (data is null || league != loadedLeague) _ = LoadAsync();
    }

    public void LeagueChanged()
    {
        data = null;
        if (Parent is not null) _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (loading) return;
        loading = true;
        int requested = ++revision;
        refresh.Enabled = false;
        refresh.Text = "Loading…";
        meta.Text = "Loading completed trades from GGG…";
        table.Placeholder = "Loading the last 24 hours of trades…";
        try
        {
            var result = await worker.SendAsync<MarketResult>(new { action = "market", preferred = preferred(), period },
                TimeSpan.FromMinutes(3));
            if (IsDisposed || requested != revision) return;
            data = result;
            loadedLeague = preferred();
            var updated = DateTimeOffset.FromUnixTimeMilliseconds(result.LatestHour + 3_600_000).ToLocalTime();
            meta.Text = $"{result.League} · {result.Rows.Length:N0} items · completed trades to {updated:t}" +
                (result.FailedHours > 0 ? $" · {result.FailedHours} hour(s) unavailable" : "");
            BuildUnitMenu();
            categories.SetCounts(Categories.Select(entry => (entry.Id, entry.Label,
                entry.Id == "All" ? result.Rows.Length : result.Rows.Count(row => row.Category == entry.Id)))
                .Where(entry => entry.Item1 == "All" || entry.Item3 > 0).ToArray(), category);
            ApplyFilter();
            detail.Refresh(result, UnitRates());
        }
        catch (Exception error)
        {
            if (!IsDisposed && requested == revision)
            {
                meta.Text = error.Message;
                table.Placeholder = "Prices could not be loaded. Check your connection and refresh.";
            }
        }
        finally
        {
            loading = false;
            if (!IsDisposed)
            {
                refresh.Enabled = true;
                refresh.Text = "Refresh";
            }
        }
    }

    private Dictionary<string, double> UnitRates()
    {
        var rates = new Dictionary<string, double> { [ChaosId] = 1 };
        foreach (var row in data?.Rows ?? []) rates[row.Id] = row.Chaos;
        return rates;
    }

    private void ApplyFilter()
    {
        if (data is null) return;
        string query = search.Input.Text.Trim();
        var rows = data.Rows.Where(row =>
            (category == "All" || row.Category == category) &&
            (query.Length == 0 || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)));
        table.Show(rows.ToArray(), unit, UnitRates(), UnitIcon, period);
        table.Placeholder = query.Length > 0 ? $"Nothing traded matches “{query}”." : "Nothing traded in this category in the last 24 hours.";
    }

    /// <summary>The icon of a unit, so prices read "214 [divine icon]".</summary>
    private Image? UnitIcon(string id) =>
        id == ChaosId ? CurrencyIcons.GetUrl(data?.ChaosIcon) : CurrencyIcons.GetUrl(data?.Rows.FirstOrDefault(row => row.Id == id)?.Icon);

    private void BuildUnitMenu()
    {
        while (unitMenu.Items.Count > 0) unitMenu.Items[0].Dispose();
        unitMenu.ShowImageMargin = true;
        void Add(ToolStripItemCollection items, string id, string label)
        {
            var option = new ToolStripMenuItem(label) { Tag = id, Checked = id == unit };
            option.Click += (_, _) => SetUnit(id, label);
            items.Add(option);
        }
        Add(unitMenu.Items, "auto", "Auto (Chaos or Divine)");
        unitMenu.Items.Add(new ToolStripSeparator());
        Add(unitMenu.Items, ChaosId, "Chaos Orb");
        foreach (var (id, label) in new[] { (DivineId, "Divine Orb"), (ExaltedId, "Exalted Orb"), (MirrorId, "Mirror of Kalandra") })
            if (data is null || data.Rows.Any(row => row.Id == id)) Add(unitMenu.Items, id, label);
        // Any other traded currency can be the unit as well.
        var others = data?.Rows.Where(row => row.Category == "Currency" &&
            row.Id is not DivineId and not ExaltedId and not MirrorId).OrderBy(row => row.Name).ToArray() ?? [];
        if (others.Length > 0)
        {
            unitMenu.Items.Add(new ToolStripSeparator());
            var more = new ToolStripMenuItem("Other currency");
            more.DropDown = WindowTheme.Menu();
            ((ContextMenuStrip)more.DropDown).ShowImageMargin = false;
            more.DropDown.MaximumSize = new Size(0, this.Px(460));
            foreach (var row in others) Add(more.DropDownItems, row.Id, row.Name);
            unitMenu.Items.Add(more);
        }
    }

    private void SetUnit(string id, string label)
    {
        unit = id;
        unitButton.Text = $"{(id == "auto" ? "Auto" : label)}  ▾";
        void Check(ToolStripItemCollection items)
        {
            foreach (var item in items.OfType<ToolStripMenuItem>())
            {
                item.Checked = Equals(item.Tag, id);
                if (item.HasDropDownItems) Check(item.DropDownItems);
            }
        }
        Check(unitMenu.Items);
        ApplyFilter();
        detail.SetUnit(id, UnitRates());
    }

    private void ShowDetail(MarketRowData row)
    {
        if (data is null) return;
        detail.Open(row, data, unit, UnitRates(), UnitIcon);
        body.SuspendLayout();
        body.Controls.Clear();
        body.Controls.Add(detail);
        body.ResumeLayout();
    }

    private void ShowList()
    {
        body.SuspendLayout();
        body.Controls.Clear();
        body.Controls.Add(listView);
        body.ResumeLayout();
    }

    public static string Amount(double value) =>
        value >= 100_000 ? (value / 1000).ToString("0") + "K"
        : value >= 10_000 ? (value / 1000).ToString("0.#") + "K"
        : value >= 1000 ? (value / 1000).ToString("0.##") + "K"
        : value >= 100 ? value.ToString("0")
        : value >= 10 ? value.ToString("0.#")
        : value >= 1 ? value.ToString("0.##")
        : value >= 0.01 ? value.ToString("0.###")
        : value.ToString("G2");

    /// <summary>Converts a Chaos value for display; Auto uses Divines once worth one.</summary>
    public static (double Amount, string Unit)? InUnit(double chaos, string unit, IReadOnlyDictionary<string, double> rates)
    {
        if (unit == "auto")
            return rates.TryGetValue(DivineId, out double divine) && chaos >= divine ? (chaos / divine, DivineId) : (chaos, ChaosId);
        return rates.TryGetValue(unit, out double rate) && rate > 0 ? (chaos / rate, unit) : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CurrencyIcons.Changed -= table.Invalidate;
            CurrencyIcons.Changed -= categories.Invalidate;
            unitMenu.Dispose();
            worker.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Category sidebar with item counts.</summary>
internal sealed class CategoryList : ScrollList
{
    private (string Id, string Label, int Count)[] entries = [];
    private string selected = "All";

    public CategoryList()
    {
        AccessibleName = "Item categories";
        AccessibleRole = AccessibleRole.List;
        RowClicked += (index, _) =>
        {
            selected = entries[index].Id;
            Invalidate();
            Chosen?.Invoke(selected);
        };
    }

    public event Action<string>? Chosen;

    public void SetCounts((string Id, string Label, int Count)[] value, string current)
    {
        entries = value;
        selected = value.Any(entry => entry.Id == current) ? current : "All";
        Invalidate();
    }

    protected override int Count => entries.Length;
    protected override int RowHeight => this.Px(34);
    protected override bool IsClickable(int index, Point local) => true;

    protected override void DrawRow(Graphics g, int index, Rectangle bounds, bool hovered)
    {
        var (id, label, count) = entries[index];
        bool active = id == selected;
        var row = Rectangle.Inflate(bounds, 0, -1);
        if (active) WindowTheme.FillRounded(g, row, this.Px(7), WindowTheme.AccentFill, WindowTheme.AccentBorder);
        else if (hovered) WindowTheme.FillRounded(g, row, this.Px(7), WindowTheme.Hover);
        var text = Rectangle.FromLTRB(row.Left + this.Px(12), row.Top, row.Right - this.Px(10), row.Bottom);
        WindowTheme.Text(g, count.ToString("N0"), WindowTheme.Mono(7.5f), text,
            active ? WindowTheme.AccentSoftText : WindowTheme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        int countWidth = TextRenderer.MeasureText(count.ToString("N0"), WindowTheme.Mono(7.5f)).Width;
        WindowTheme.Text(g, label, active ? WindowTheme.Sans(9.5f, FontStyle.Bold) : WindowTheme.Sans(9.5f),
            Rectangle.FromLTRB(text.Left, text.Top, text.Right - countWidth - this.Px(6), text.Bottom),
            active ? WindowTheme.AccentSoftText : hovered ? WindowTheme.Body : WindowTheme.Secondary);
    }
}

/// <summary>The market table: icon, name, price in the chosen unit, volume, sparkline and change.</summary>
internal sealed class MarketTable : ScrollList
{
    private enum Sort { Price, Volume, Change, Name }
    private MarketRowData[] rows = [];
    private MarketRowData[] shown = [];
    private string unit = "auto";
    private IReadOnlyDictionary<string, double> rates = new Dictionary<string, double>();
    private Func<string, Image?> unitIcon = _ => null;
    private string period = "24h";
    private Sort sort = Sort.Price;
    private bool descending = true;

    public MarketTable()
    {
        AccessibleName = "Currency market";
        AccessibleRole = AccessibleRole.Table;
        RowClicked += (index, _) => Opened?.Invoke(shown[index]);
    }

    public event Action<MarketRowData>? Opened;
    public event Action? SortChanged;

    public void Show(MarketRowData[] value, string unit, IReadOnlyDictionary<string, double> rates,
        Func<string, Image?> unitIcon, string period)
    {
        rows = value;
        this.unit = unit;
        this.rates = rates;
        this.unitIcon = unitIcon;
        this.period = period;
        Order();
        Reset();
    }

    private void Order()
    {
        IEnumerable<MarketRowData> ordered = sort switch
        {
            Sort.Volume => rows.OrderBy(row => row.Volume),
            Sort.Change => rows.OrderBy(row => row.Change ?? double.MinValue),
            Sort.Name => rows.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderBy(row => row.Chaos),
        };
        shown = (descending ? ordered.Reverse() : ordered).ToArray();
    }

    protected override int Count => shown.Length;
    protected override int RowHeight => this.Px(46);
    protected override int HeaderHeight => this.Px(30);
    protected override bool IsClickable(int index, Point local) => true;

    /// <summary>
    /// Fixed columns on the right; the name takes the rest. When the window is
    /// too narrow to keep a readable name, the sparkline goes first, then the
    /// volume, rather than squeezing the item name out of view.
    /// </summary>
    private (Rectangle Icon, Rectangle Name, Rectangle Price, Rectangle Volume, Rectangle Spark, Rectangle Change) Columns(Rectangle row)
    {
        int pad = this.Px(8), gap = this.Px(10);
        int change = this.Px(64), price = this.Px(104), icon = this.Px(30);
        int volume = this.Px(78), spark = this.Px(104), minName = this.Px(170);
        int fixedWidth = pad * 2 + icon + this.Px(12) + price + gap + change;
        bool showVolume = row.Width - fixedWidth - (volume + gap) >= minName;
        bool showSpark = showVolume && row.Width - fixedWidth - (volume + gap) - (spark + gap) >= minName;
        int right = row.Right - pad;
        var changeBox = new Rectangle(right - change, row.Top, change, row.Height);
        int x = changeBox.Left - gap;
        var sparkBox = showSpark ? new Rectangle(x - spark, row.Top, spark, row.Height) : Rectangle.Empty;
        if (showSpark) x = sparkBox.Left - gap;
        var volumeBox = showVolume ? new Rectangle(x - volume, row.Top, volume, row.Height) : Rectangle.Empty;
        if (showVolume) x = volumeBox.Left - gap;
        var priceBox = new Rectangle(x - price, row.Top, price, row.Height);
        var iconBox = new Rectangle(row.Left + pad, row.Top, icon, row.Height);
        var nameBox = Rectangle.FromLTRB(iconBox.Right + this.Px(12), row.Top, priceBox.Left - gap, row.Bottom);
        return (iconBox, nameBox, priceBox, volumeBox, sparkBox, changeBox);
    }

    protected override void DrawHeader(Graphics g, Rectangle bounds)
    {
        var (_, name, price, volume, spark, change) = Columns(new Rectangle(0, 0, Width - this.Px(8), bounds.Height));
        var font = WindowTheme.Mono(7f);
        void Head(string label, Rectangle box, Sort? key, TextFormatFlags align)
        {
            string arrow = key == sort ? (descending ? " ▼" : " ▲") : "";
            WindowTheme.Text(g, label + arrow, font, box, key == sort ? WindowTheme.Body : WindowTheme.Faint, align | TextFormatFlags.VerticalCenter);
        }
        Head("ITEM", name, Sort.Name, TextFormatFlags.Left);
        Head("PRICE", price, Sort.Price, TextFormatFlags.Right);
        if (!volume.IsEmpty) Head("TRADED 24H", volume, Sort.Volume, TextFormatFlags.Right);
        if (!spark.IsEmpty) Head(period == "7d" ? "7 DAYS" : "24 HOURS", spark, null, TextFormatFlags.HorizontalCenter);
        Head("CHANGE", change, Sort.Change, TextFormatFlags.Right);
        using var line = new Pen(WindowTheme.Hairline);
        g.DrawLine(line, 0, bounds.Bottom - 1, Width - this.Px(8), bounds.Bottom - 1);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Y < HeaderHeight && e.Button == MouseButtons.Left)
        {
            var (_, name, price, volume, _, change) = Columns(new Rectangle(0, 0, Width - this.Px(8), HeaderHeight));
            Sort? key = name.Contains(e.Location) ? Sort.Name : price.Contains(e.Location) ? Sort.Price
                : !volume.IsEmpty && volume.Contains(e.Location) ? Sort.Volume : change.Contains(e.Location) ? Sort.Change : null;
            if (key is { } chosen)
            {
                descending = chosen == sort ? !descending : chosen != Sort.Name;
                sort = chosen;
                Order();
                Invalidate();
                SortChanged?.Invoke();
            }
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void DrawRow(Graphics g, int index, Rectangle bounds, bool hovered)
    {
        var row = shown[index];
        var card = Rectangle.Inflate(bounds, 0, -this.Px(2));
        WindowTheme.FillRounded(g, card, this.Px(8), hovered ? WindowTheme.Hover : WindowTheme.Chip,
            hovered ? WindowTheme.BorderStrong : WindowTheme.Hairline);
        var (iconBox, name, price, volume, spark, change) = Columns(card);

        int iconSize = this.Px(30);
        var tile = new Rectangle(iconBox.Left, card.Top + (card.Height - iconSize) / 2, iconSize, iconSize);
        WindowTheme.FillRounded(g, tile, this.Px(6), WindowTheme.Sunken);
        if (CurrencyIcons.GetUrl(row.Icon) is { } image)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, Rectangle.Inflate(tile, -this.Px(2), -this.Px(2)));
        }
        WindowTheme.Text(g, row.Name, WindowTheme.Sans(10f, FontStyle.Bold), name, WindowTheme.Foreground);

        // Price: amount followed by the unit's own icon (or its name when no icon).
        if (MarketPage.InUnit(row.Chaos, unit, rates) is var (amount, unitId))
        {
            int unitSize = this.Px(18);
            var unitImage = unitIcon(unitId);
            int unitSpace = unitImage is null ? 0 : unitSize + this.Px(5);
            var amountBox = Rectangle.FromLTRB(price.Left, price.Top, price.Right - unitSpace, price.Bottom);
            string text = MarketPage.Amount(amount);
            if (unitImage is not null)
            {
                g.DrawImage(unitImage, new Rectangle(price.Right - unitSize, price.Top + (price.Height - unitSize) / 2, unitSize, unitSize));
            }
            else text += unitId == MarketPage.ChaosId ? " c" : unitId == MarketPage.DivineId ? " div" : "";
            WindowTheme.Text(g, text, WindowTheme.Mono(10f, FontStyle.Bold), amountBox, WindowTheme.Foreground,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
        else
        {
            WindowTheme.Text(g, "—", WindowTheme.Mono(10f), price, WindowTheme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        if (!volume.IsEmpty)
            WindowTheme.Text(g, row.Volume >= 10_000 ? (row.Volume / 1000).ToString("0.#") + "K" : row.Volume.ToString("N0"),
                WindowTheme.Mono(8.5f), volume, WindowTheme.Secondary, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        if (!spark.IsEmpty)
            Sparkline.Draw(g, Rectangle.Inflate(spark, 0, -this.Px(10)), row.Series, row.Change, DeviceDpi);
        string delta = row.Change is { } pct ? $"{(pct >= 0 ? "+" : "")}{pct:0.#}%" : "—";
        WindowTheme.Text(g, delta, WindowTheme.Mono(8.5f, FontStyle.Bold), change,
            row.Change is null ? WindowTheme.Faint : row.Change >= 0 ? WindowTheme.UpText : WindowTheme.Down,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }
}

/// <summary>A small price line; rising in olive, falling in ember, as on macOS.</summary>
internal static class Sparkline
{
    public static void Draw(Graphics g, Rectangle box, IReadOnlyList<double?> series, double? change, int dpi)
    {
        var points = series.Select((value, index) => (value, index)).Where(point => point.value is not null).ToArray();
        if (points.Length < 2 || box.Width < 4 || box.Height < 4)
        {
            using var dash = new Pen(WindowTheme.Hairline) { DashStyle = DashStyle.Dot };
            g.DrawLine(dash, box.Left, box.Top + box.Height / 2, box.Right, box.Top + box.Height / 2);
            return;
        }
        double low = points.Min(point => point.value!.Value), high = points.Max(point => point.value!.Value);
        double span = Math.Max(high - low, high * 0.001);
        float X(int index) => box.Left + index / (float)Math.Max(1, series.Count - 1) * box.Width;
        float Y(double value) => box.Bottom - (float)((value - low) / span) * box.Height;
        var line = points.Select(point => new PointF(X(point.index), Y(point.value!.Value))).ToArray();
        var color = change is null ? WindowTheme.Muted : change >= 0 ? WindowTheme.Up : WindowTheme.Down;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var area = new GraphicsPath())
        {
            area.AddLines(line);
            area.AddLine(line[^1], new PointF(line[^1].X, box.Bottom));
            area.AddLine(new PointF(line[^1].X, box.Bottom), new PointF(line[0].X, box.Bottom));
            area.CloseFigure();
            using var fill = new LinearGradientBrush(new Rectangle(box.Left, box.Top - 1, box.Width, box.Height + 2),
                Color.FromArgb(70, color), Color.FromArgb(0, color), LinearGradientMode.Vertical);
            g.FillPath(fill, area);
        }
        using var pen = new Pen(color, 1.6f * dpi / 96) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, line);
    }
}

/// <summary>One item's page: its price, stats and a large chart in any unit.</summary>
internal sealed class MarketDetail : UserControl
{
    private readonly Worker worker;
    private readonly Func<string?> preferred;
    private readonly FlatButton back = new(ButtonKind.Ghost, "←  All items");
    private readonly ItemHeader header = new() { Dock = DockStyle.Fill };
    private readonly Label stats = Ui.Label("", WindowTheme.Sans(9f), WindowTheme.Secondary);
    private readonly Label status = Ui.Label("", WindowTheme.Sans(8f), WindowTheme.Faint);
    private readonly HistoryPlot plot = new() { Dock = DockStyle.Fill };
    private readonly List<(FlatButton Button, string Period)> periods = [];
    private MarketRowData? row;
    private MarketResult? data;
    private IReadOnlyDictionary<string, double> rates = new Dictionary<string, double>();
    private string unit = "auto";
    private string period = "7d";
    private int revision;

    public event Action? Back;

    public MarketDetail(Worker worker, Func<string?> preferred)
    {
        this.worker = worker;
        this.preferred = preferred;
        BackColor = WindowTheme.Background;
        int S(int value) => this.Px(value);
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(S(18), S(14), S(18), S(12)), Margin = new Padding(0, S(14), 0, 0) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(76)));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        back.Margin = new Padding(0, 0, 0, S(8));
        back.Click += (_, _) => Back?.Invoke();
        layout.Controls.Add(back, 0, 0);
        layout.Controls.Add(header, 0, 1);
        stats.Margin = new Padding(0, S(2), 0, S(10));
        layout.Controls.Add(stats, 0, 2);
        var chips = Ui.Row();
        chips.Margin = new Padding(0, 0, 0, S(8));
        foreach (string value in new[] { "24h", "7d", "30d" })
        {
            var button = new FlatButton(ButtonKind.Chip, value) { Selected = value == period };
            button.Click += (_, _) =>
            {
                period = value;
                foreach (var (other, id) in periods) other.Selected = id == period;
                _ = LoadAsync();
            };
            periods.Add((button, value));
            chips.Controls.Add(button);
        }
        layout.Controls.Add(chips, 0, 3);
        layout.Controls.Add(plot, 0, 4);
        status.Margin = new Padding(0, S(6), 0, 0);
        layout.Controls.Add(status, 0, 5);
        card.Controls.Add(layout);
        Controls.Add(card);
    }

    public void Open(MarketRowData value, MarketResult snapshot, string unitId, IReadOnlyDictionary<string, double> unitRates,
        Func<string, Image?> unitIcon)
    {
        row = value;
        data = snapshot;
        rates = unitRates;
        unit = unitId;
        header.Unit = unitIcon;
        Render();
        _ = LoadAsync();
    }

    public void SetUnit(string unitId, IReadOnlyDictionary<string, double> unitRates)
    {
        unit = unitId;
        rates = unitRates;
        if (row is null || Parent is null) return;
        Render();
        _ = LoadAsync();
    }

    public void Refresh(MarketResult snapshot, IReadOnlyDictionary<string, double> unitRates)
    {
        data = snapshot;
        rates = unitRates;
        if (row is null) return;
        row = snapshot.Rows.FirstOrDefault(entry => entry.Id == row.Id) ?? row;
        if (Parent is not null) Render();
    }

    /// <summary>The chart needs a fixed unit: Auto resolves to Divine or Chaos for this item.</summary>
    private string ChartUnit()
    {
        if (row is null) return MarketPage.ChaosId;
        string resolved = MarketPage.InUnit(row.Chaos, unit, rates)?.Unit ?? MarketPage.ChaosId;
        return resolved == row.Id ? MarketPage.ChaosId : resolved;
    }

    private void Render()
    {
        if (row is null) return;
        string chart = ChartUnit();
        header.Show(row, MarketPage.InUnit(row.Chaos, chart, rates), UnitName(chart));
        string divine = rates.TryGetValue(MarketPage.DivineId, out double div) && div > 0
            ? $" · {MarketPage.Amount(row.Chaos / div)} Divine" : "";
        string change = row.Change is { } pct ? $"{(pct >= 0 ? "+" : "")}{pct:0.#}% over the table period" : "change unavailable";
        stats.Text = $"{MarketPage.Amount(row.Chaos)} Chaos{divine} · {row.Volume:N0} traded in 24h · {change}";
        plot.Unit = UnitName(chart);
    }

    private string UnitName(string id) =>
        id == MarketPage.ChaosId ? "Chaos Orb" : data?.Rows.FirstOrDefault(entry => entry.Id == id)?.Name ?? "Chaos Orb";

    private async Task LoadAsync()
    {
        if (row is null) return;
        int requested = ++revision;
        string chart = ChartUnit();
        plot.Points = [];
        plot.Placeholder = "Loading history…";
        status.Text = period switch
        {
            "24h" => "Each point is one hour of GGG completed trades.",
            "7d" => "Each point combines four sampled hours of one day.",
            _ => "Each point is one sampled hour per day; quiet days stay gaps.",
        };
        try
        {
            var result = await worker.SendAsync<ItemHistoryResult>(
                new { action = "itemHistory", preferred = preferred(), item = row.Id, unit = chart, period },
                TimeSpan.FromMinutes(3));
            if (IsDisposed || requested != revision) return;
            plot.Points = result.Points.Select(point => new HistoryPoint(point.At, point.Value, point.Volume, null)).ToArray();
            plot.Placeholder = "No trades in this period.";
            if (result.FailedHours > 0) status.Text += $" {result.FailedHours} hour(s) could not be loaded.";
        }
        catch (Exception error)
        {
            if (!IsDisposed && requested == revision) plot.Placeholder = error.Message;
        }
    }
}

/// <summary>Icon, name and category on the left; the price in the chart's unit on the right.</summary>
internal sealed class ItemHeader : Control
{
    private MarketRowData? row;
    private (double Amount, string Unit)? price;
    private string unitName = "";
    public Func<string, Image?> Unit { get; set; } = _ => null;

    public ItemHeader()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint, true);
        BackColor = WindowTheme.Card;
        CurrencyIcons.Changed += Invalidate;
    }

    public void Show(MarketRowData value, (double Amount, string Unit)? inUnit, string name)
    {
        row = value;
        price = inUnit;
        unitName = name;
        AccessibleName = $"{value.Name}, {(inUnit is { } p ? MarketPage.Amount(p.Amount) + " " + name : "no price")}";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (row is null) return;
        int icon = this.Px(56);
        var tile = new Rectangle(0, (Height - icon) / 2, icon, icon);
        WindowTheme.FillRounded(g, tile, this.Px(10), WindowTheme.Sunken, WindowTheme.Border);
        if (CurrencyIcons.GetUrl(row.Icon) is { } image)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, Rectangle.Inflate(tile, -this.Px(6), -this.Px(6)));
        }
        string category = MarketPage.Categories.FirstOrDefault(entry => entry.Id == row.Category).Label ?? row.Category;
        int priceWidth = 0;
        if (price is var (amount, unitId))
        {
            string text = MarketPage.Amount(amount);
            var big = WindowTheme.Mono(22f, FontStyle.Bold);
            Size size = TextRenderer.MeasureText(g, text, big, Size.Empty, TextFormatFlags.NoPadding);
            int unitSize = this.Px(30);
            var unitImage = Unit(unitId);
            int x = Width - size.Width - (unitImage is null ? 0 : unitSize + this.Px(8));
            int top = (Height - size.Height) / 2 - this.Px(6);
            TextRenderer.DrawText(g, text, big, new Point(x, top), Color.White, TextFormatFlags.NoPadding);
            if (unitImage is not null)
                g.DrawImage(unitImage, new Rectangle(Width - unitSize, top + (size.Height - unitSize) / 2, unitSize, unitSize));
            string caption = $"in {unitName}";
            var small = WindowTheme.Sans(8.5f);
            Size captionSize = TextRenderer.MeasureText(caption, small);
            TextRenderer.DrawText(g, caption, small, new Point(Width - captionSize.Width, top + size.Height + this.Px(2)), WindowTheme.Dim);
            priceWidth = size.Width + unitSize + this.Px(24);
        }
        var text2 = Rectangle.FromLTRB(tile.Right + this.Px(16), 0, Width - priceWidth, Height);
        WindowTheme.Text(g, row.Name, WindowTheme.Display(17f), new Rectangle(text2.Left, text2.Top + this.Px(10), text2.Width, this.Px(34)),
            WindowTheme.Foreground, TextFormatFlags.Top);
        WindowTheme.Text(g, category.ToUpperInvariant(), WindowTheme.Mono(7.5f),
            new Rectangle(text2.Left, text2.Top + this.Px(44), text2.Width, this.Px(18)), WindowTheme.AccentText, TextFormatFlags.Top);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) CurrencyIcons.Changed -= Invalidate;
        base.Dispose(disposing);
    }
}
