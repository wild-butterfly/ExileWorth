using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ExileWorth.Windows;

internal static class WindowTheme
{
    public static readonly Color Background = Color.FromArgb(20, 16, 17);
    public static readonly Color Surface = Color.FromArgb(31, 25, 26);
    public static readonly Color Foreground = Color.FromArgb(247, 239, 236);
    public static readonly Color Muted = Color.FromArgb(195, 181, 174);
    public static readonly Color Accent = Color.FromArgb(255, 187, 193);
    public static readonly Color Border = Color.FromArgb(79, 65, 63);
    public static readonly Color Hover = Color.FromArgb(63, 36, 40);
    public static readonly Color Action = Color.FromArgb(165, 18, 41);

    public static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        if (diameter < 2)
        {
            path.AddRectangle(bounds);
            return path;
        }
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void StyleFilters(CheckedListBox filters)
    {
        filters.BackColor = Surface;
        filters.ForeColor = Foreground;
        filters.BorderStyle = BorderStyle.None;
        filters.IntegralHeight = false;
    }

    public static void StyleListings(DataGridView grid)
    {
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.GridColor = Color.FromArgb(53, 43, 43);
        grid.EnableHeadersVisualStyles = false;
        grid.RowHeadersVisible = false;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface, ForeColor = Foreground, SelectionBackColor = Hover,
            SelectionForeColor = Foreground, Padding = new Padding(5, 4, 5, 4),
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Background, ForeColor = Muted, SelectionBackColor = Background,
            SelectionForeColor = Muted, Padding = new Padding(5, 4, 5, 4),
        };
        foreach (var (name, label, weight) in new[]
        {
            ("price", "Price", 26), ("level", "iLvl", 13),
            ("account", "Account", 42), ("status", "Status", 19),
        })
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = label, FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
        }
        grid.Columns[0].DefaultCellStyle.ForeColor = Accent;
        grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex == 3 && e.Value is string presence)
                e.CellStyle!.ForeColor = presence.Equals("online", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb(179, 204, 145) : Muted;
        };
    }

    public static void ApplyTitleBar(nint handle)
    {
        int enabled = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        int rounded = 2;
        _ = DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}

internal sealed class GlassPanel : Panel
{
    private readonly bool accent;

    public GlassPanel(bool accent = false)
    {
        this.accent = accent;
        DoubleBuffered = true;
        ResizeRedraw = true;
        Padding = new Padding(12);
        Margin = new Padding(0, 0, 0, 8);
        BackColor = Color.Transparent;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (!AutoSize || Controls.Count == 0) return base.GetPreferredSize(proposedSize);
        Size content = Controls[0].GetPreferredSize(new Size(
            Math.Max(0, proposedSize.Width - Padding.Horizontal), 0));
        return content + Padding.Size;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        if (bounds.Width < 1 || bounds.Height < 1) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = WindowTheme.Rounded(bounds, 12 * DeviceDpi / 96);
        using var fill = new LinearGradientBrush(bounds,
            accent ? Color.FromArgb(74, 36, 39) : Color.FromArgb(45, 37, 37),
            WindowTheme.Surface, LinearGradientMode.ForwardDiagonal);
        using var edge = new Pen(WindowTheme.Border);
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(edge, shape);
    }
}

internal sealed class GlassButton : Button
{
    private readonly bool primary;
    private bool hovered;

    public GlassButton(bool primary = false)
    {
        this.primary = primary;
        FlatStyle = FlatStyle.Flat;
        Padding = new Padding(12, 6, 12, 6);
        Margin = new Padding(0, 0, 8, 0);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        if (bounds.Width < 1 || bounds.Height < 1) return;
        e.Graphics.Clear(Parent?.BackColor ?? WindowTheme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = WindowTheme.Rounded(bounds, 8 * DeviceDpi / 96);
        Color color = !Enabled ? WindowTheme.Surface
            : primary ? (hovered ? Color.FromArgb(195, 23, 49) : WindowTheme.Action)
            : hovered ? WindowTheme.Hover : WindowTheme.Surface;
        using var fill = new SolidBrush(color);
        using var edge = new Pen(Focused && ShowFocusCues ? WindowTheme.Accent : WindowTheme.Border);
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(edge, shape);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds,
            Enabled ? WindowTheme.Foreground : WindowTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class ModifierList : CheckedListBox
{
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        bool isChecked = GetItemChecked(e.Index);
        var bounds = Rectangle.Inflate(e.Bounds, -1, -2);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var row = WindowTheme.Rounded(bounds, 6 * DeviceDpi / 96);
        using var fill = new SolidBrush(selected ? WindowTheme.Hover : Color.FromArgb(43, 35, 35));
        using var edge = new Pen(WindowTheme.Border);
        e.Graphics.FillPath(fill, row);
        e.Graphics.DrawPath(edge, row);
        int side = Math.Min(14 * DeviceDpi / 96, bounds.Height - 2);
        if (side < 1) return;
        var box = new Rectangle(bounds.Left + 7, bounds.Top + (bounds.Height - side) / 2, side, side);
        using var mark = WindowTheme.Rounded(box, 4 * DeviceDpi / 96);
        using var accent = new SolidBrush(WindowTheme.Action);
        if (isChecked)
        {
            e.Graphics.FillPath(accent, mark);
            using var tick = new Pen(WindowTheme.Foreground, 1.5f);
            e.Graphics.DrawLines(tick, [
                new Point(box.Left + side / 4, box.Top + side / 2),
                new Point(box.Left + side * 2 / 5, box.Top + side * 2 / 3),
                new Point(box.Left + side * 3 / 4, box.Top + side / 3),
            ]);
        }
        else e.Graphics.DrawPath(edge, mark);
        var label = Rectangle.FromLTRB(box.Right + 7, bounds.Top, bounds.Right - 6, bounds.Bottom);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, label,
            isChecked ? WindowTheme.Foreground : WindowTheme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        e.DrawFocusRectangle();
    }

    public override int ItemHeight => Font.Height + 12 * DeviceDpi / 96;
}
