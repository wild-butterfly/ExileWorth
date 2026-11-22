using System.Drawing.Drawing2D;

namespace ExileWorth.Windows;

internal record HistoryPoint(long At, double? Rate, double? GiveVolume, double? WantVolume);

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
