using System.Runtime.InteropServices;

namespace ExileWorth.Windows;

internal static class WindowTheme
{
    public static readonly Color Background = Color.FromArgb(19, 16, 18);
    public static readonly Color Surface = Color.FromArgb(33, 28, 31);
    public static readonly Color Foreground = Color.FromArgb(247, 239, 241);
    public static readonly Color Muted = Color.FromArgb(199, 186, 191);
    private static readonly Color Border = Color.FromArgb(86, 68, 75);
    private static readonly Color Action = Color.FromArgb(145, 26, 49);
    private static readonly Color Hover = Color.FromArgb(66, 38, 47);
    private static readonly Color Accent = Color.FromArgb(255, 157, 170);

    public static void StyleButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = primary ? Action : Surface;
        button.ForeColor = Foreground;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderColor = primary ? Action : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(166, 33, 59) : Hover;
        button.Padding = new Padding(12, 5, 12, 5);
        button.Margin = new Padding(0, 0, 8, 4);
    }

    public static void StyleLeague(ComboBox league)
    {
        league.FlatStyle = FlatStyle.Flat;
        league.DrawMode = DrawMode.OwnerDrawFixed;
        league.BackColor = Surface;
        league.ForeColor = Foreground;
        league.Margin = new Padding(0, 4, 8, 4);
        league.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var brush = new SolidBrush(selected ? Hover : Surface);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, league.GetItemText(league.Items[e.Index]), league.Font,
                Rectangle.Inflate(e.Bounds, -5, 0), league.Enabled ? Foreground : Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        };
    }

    public static void StyleListings(ListView list)
    {
        list.BackColor = Surface;
        list.ForeColor = Foreground;
        list.BorderStyle = BorderStyle.None;
        list.OwnerDraw = true;
        list.DrawColumnHeader += (_, e) => DrawCell(e.Graphics, e.Bounds, e.Font ?? list.Font,
            e.Header?.Text ?? "", Background, Muted);
        list.DrawSubItem += (_, e) =>
        {
            bool selected = e.Item?.Selected == true;
            Color background = selected ? Hover : e.ItemIndex % 2 == 0 ? Surface : Background;
            DrawCell(e.Graphics, e.Bounds, list.Font, e.SubItem?.Text ?? "", background,
                e.ColumnIndex == 0 ? Accent : Foreground);
            if (e.Item?.Focused == true && list.Focused)
                ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, Foreground, background);
        };
    }

    private static void DrawCell(Graphics graphics, Rectangle bounds, Font font, string text,
        Color background, Color foreground)
    {
        using var brush = new SolidBrush(background);
        graphics.FillRectangle(brush, bounds);
        TextRenderer.DrawText(graphics, text, font, Rectangle.Inflate(bounds, -5, 0), foreground,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    public static void ApplyTitleBar(nint handle)
    {
        const int useImmersiveDarkMode = 20;
        int enabled = 1;
        _ = DwmSetWindowAttribute(handle, useImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
