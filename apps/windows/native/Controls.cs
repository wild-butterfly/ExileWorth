using System.Drawing.Drawing2D;

namespace ExileWorth.Windows;

/// <summary>
/// A rounded surface. Its BackColor is the fill, so child controls inherit it
/// and blend in without any transparency tricks.
/// </summary>
internal class Card : Panel
{
    public int Radius { get; set; } = 10;
    public Color Edge { get; set; } = WindowTheme.Border;
    public bool RimLight { get; set; } = true;

    public Card()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = WindowTheme.Card;
        Padding = new Padding(16);
        Margin = new Padding(0, 0, 0, 12);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? WindowTheme.Background);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = this.Px(Radius);
        WindowTheme.FillRounded(e.Graphics, bounds, radius, BackColor, Edge);
        if (!RimLight || bounds.Width < radius * 2) return;
        // The top rim light that makes a surface read as glass on macOS.
        using var rim = new Pen(WindowTheme.RimLight);
        e.Graphics.DrawLine(rim, bounds.Left + radius, bounds.Top, bounds.Right - radius, bounds.Top);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (!AutoSize || Controls.Count == 0) return base.GetPreferredSize(proposedSize);
        Size content = Controls[0].GetPreferredSize(new Size(Math.Max(0, proposedSize.Width - Padding.Horizontal), 0));
        return new Size(proposedSize.Width > 0 ? proposedSize.Width : content.Width + Padding.Horizontal,
            content.Height + Padding.Vertical);
    }
}

internal enum ButtonKind { Primary, Secondary, Ghost, Nav, Tab, Chip, Icon, Accent }

/// <summary>One owner-drawn button for every role, so states look the same everywhere.</summary>
internal sealed class FlatButton : Button
{
    private bool hovered, pressed;
    private bool selected;
    private Image? image;
    private string? badge;

    public ButtonKind Kind { get; }
    public string? Glyph { get; init; }
    public string? Hint { get; set; }

    public bool Selected
    {
        get => selected;
        set { selected = value; AccessibleDescription = value ? "Selected" : null; Invalidate(); }
    }

    public Image? Icon
    {
        get => image;
        set { image = value; if (AutoSize) PerformLayout(); Invalidate(); }
    }

    public string? Badge
    {
        get => badge;
        set { badge = value; Invalidate(); }
    }

    public FlatButton(ButtonKind kind, string text = "")
    {
        Kind = kind;
        Text = text;
        AccessibleName = text;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        AutoSize = kind != ButtonKind.Nav;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = kind switch
        {
            ButtonKind.Nav => new Padding(0, 0, 0, 3),
            ButtonKind.Tab or ButtonKind.Chip or ButtonKind.Icon or ButtonKind.Accent => new Padding(0, 0, 4, 0),
            _ => new Padding(0, 0, 8, 0),
        };
        Font = kind switch
        {
            ButtonKind.Primary or ButtonKind.Tab => WindowTheme.Sans(9.5f, FontStyle.Bold),
            ButtonKind.Accent => WindowTheme.Sans(8.5f, FontStyle.Bold),
            ButtonKind.Chip => WindowTheme.Sans(8.5f),
            ButtonKind.Icon => WindowTheme.Sans(10f),
            _ => WindowTheme.Sans(9.5f),
        };
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = string.IsNullOrEmpty(Text) ? Size.Empty
            : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        int icon = image is null ? 0 : this.Px(Kind == ButtonKind.Chip ? 16 : 18);
        int gap = icon > 0 && text.Width > 0 ? this.Px(5) : 0;
        (int padX, int height) = Kind switch
        {
            ButtonKind.Primary or ButtonKind.Secondary or ButtonKind.Ghost => (16, 34),
            ButtonKind.Tab => (12, 26),
            ButtonKind.Chip => (icon > 0 && text.Width == 0 ? 5 : 8, 24),
            ButtonKind.Accent => (12, 24),
            ButtonKind.Icon => (0, 26),
            _ => (12, 38),
        };
        if (Kind == ButtonKind.Icon) return new Size(this.Px(26), this.Px(26));
        int badgeWidth = badge is null ? 0 : this.Px(22);
        return new Size(text.Width + icon + gap + badgeWidth + this.Px(padX) * 2, this.Px(height));
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AccessibleName ??= Text; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? WindowTheme.Background);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        if (bounds.Width < 2 || bounds.Height < 2) return;
        if (Kind == ButtonKind.Nav)
        {
            NavRow.Paint(g, bounds, Text, Glyph, Hint, selected, hovered, Focused && ShowFocusCues,
                Parent?.BackColor ?? WindowTheme.Rail, DeviceDpi / 96f, 1f);
            return;
        }

        (Color? fill, Color? edge, Color text) = Kind switch
        {
            ButtonKind.Primary or ButtonKind.Accent => (
                (Color?)(!Enabled ? WindowTheme.AccentFill : hovered ? WindowTheme.ActionHover : WindowTheme.Action),
                (Color?)(Enabled ? WindowTheme.ActionEdge : WindowTheme.AccentBorder),
                Enabled ? WindowTheme.ActionText : WindowTheme.Muted),
            ButtonKind.Secondary => (
                hovered && Enabled ? WindowTheme.Hover : WindowTheme.Raised,
                selected ? WindowTheme.AccentBorder : WindowTheme.BorderStrong,
                Enabled ? WindowTheme.Body : WindowTheme.Faint),
            ButtonKind.Ghost => (
                hovered && Enabled ? WindowTheme.Hover : null,
                null,
                Enabled ? WindowTheme.Secondary : WindowTheme.Faint),
            ButtonKind.Nav or ButtonKind.Tab => (
                selected ? (hovered ? WindowTheme.AccentFillHover : WindowTheme.AccentFill) : hovered ? WindowTheme.Hover : null,
                selected ? WindowTheme.AccentBorder : null,
                selected ? WindowTheme.AccentSoftText : hovered ? WindowTheme.Body : WindowTheme.Muted),
            ButtonKind.Chip => (
                selected ? WindowTheme.AccentFill : hovered && Enabled ? WindowTheme.Hover : WindowTheme.Chip,
                selected ? WindowTheme.AccentBorder : WindowTheme.Border,
                selected ? WindowTheme.AccentSoftText : Enabled ? WindowTheme.Secondary : WindowTheme.Faint),
            _ => (
                hovered && Enabled ? WindowTheme.Hover : null,
                null,
                Enabled ? (hovered ? WindowTheme.Foreground : WindowTheme.Muted) : WindowTheme.Faint),
        };
        if (pressed && Enabled && fill is { } pressedFill)
            fill = ControlPaint.Dark(pressedFill, 0.02f);
        if (Focused && ShowFocusCues) edge = WindowTheme.AccentText;

        int radius = this.Px(Kind switch { ButtonKind.Nav => 9, ButtonKind.Chip or ButtonKind.Tab or ButtonKind.Icon or ButtonKind.Accent => 6, _ => 8 });
        if (fill is { } solid) WindowTheme.FillRounded(g, bounds, radius, solid, edge);
        else if (edge is { } line) WindowTheme.FillRounded(g, bounds, radius, Parent?.BackColor ?? WindowTheme.Background, line);

        var content = bounds;
        Size measured = string.IsNullOrEmpty(Text) ? Size.Empty
            : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        int iconSize = image is null ? 0 : this.Px(Kind == ButtonKind.Chip ? 16 : 18);
        int gap = iconSize > 0 && measured.Width > 0 ? this.Px(5) : 0;
        int badgeWidth = badge is null ? 0 : this.Px(22);
        int total = iconSize + gap + measured.Width + badgeWidth;
        int x = bounds.Left + (bounds.Width - total) / 2;
        if (image is not null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var target = new Rectangle(x, bounds.Top + (bounds.Height - iconSize) / 2, iconSize, iconSize);
            if (Enabled) g.DrawImage(image, target);
            else ControlPaint.DrawImageDisabled(g, image, target.X, target.Y, Parent?.BackColor ?? WindowTheme.Background);
            x += iconSize + gap;
        }
        if (measured.Width > 0)
        {
            WindowTheme.Text(g, Text, Font, new Rectangle(x, bounds.Top, measured.Width + 2, bounds.Height + 1), text);
            x += measured.Width;
        }
        if (badge is not null) DrawBadge(g, new Rectangle(x, bounds.Top, badgeWidth, bounds.Height));
    }

    private void DrawBadge(Graphics g, Rectangle area)
    {
        if (badge is null) return;
        int height = this.Px(15);
        int width = Math.Max(height, TextRenderer.MeasureText(badge, WindowTheme.Mono(7f, FontStyle.Bold)).Width + this.Px(4));
        var pill = new Rectangle(area.Right - width, area.Top + (area.Height - height) / 2, width, height);
        WindowTheme.FillRounded(g, pill, height / 2, Color.FromArgb(235, 230, 228));
        WindowTheme.Text(g, badge, WindowTheme.Mono(7f, FontStyle.Bold), pill, Color.FromArgb(120, 6, 15),
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
    }
}

/// <summary>
/// One sidebar row. Shared by the live control and the --render-sidebar
/// diagnostic, so the diagnostic shows exactly what users get at each scale.
/// </summary>
internal static class NavRow
{
    /// <param name="scale">Pixel scale (DPI / 96).</param>
    /// <param name="fontScale">1 for a live control, whose fonts already follow its DPI;
    /// equal to <paramref name="scale"/> when emulating a scale on a 96-DPI bitmap.</param>
    public static Layout Paint(Graphics g, Rectangle bounds, string text, string? glyph, string? hint,
        bool selected, bool hovered, bool focused, Color back, float scale, float fontScale)
    {
        int Px(float value) => (int)Math.Round(value * scale);
        Color? fill = selected ? (hovered ? WindowTheme.AccentFillHover : WindowTheme.AccentFill) : hovered ? WindowTheme.Hover : null;
        Color? edge = focused ? WindowTheme.AccentText : selected ? WindowTheme.AccentBorder : null;
        if (fill is { } solid) WindowTheme.FillRounded(g, bounds, Px(9), solid, edge);
        else if (edge is { } line) WindowTheme.FillRounded(g, bounds, Px(9), back, line);

        var content = Rectangle.FromLTRB(bounds.Left + Px(12), bounds.Top, bounds.Right - Px(10), bounds.Bottom);
        var layout = new Layout();
        if (glyph is not null)
        {
            int size = Px(16);
            var iconBox = new Rectangle(content.Left, content.Top + (content.Height - size) / 2, size, size);
            LineIcons.Draw(g, glyph, iconBox, selected ? WindowTheme.AccentSoftText : hovered ? WindowTheme.Body : WindowTheme.Dim,
                (int)Math.Round(96 * scale));
            content = Rectangle.FromLTRB(iconBox.Right + Px(12), content.Top, content.Right, content.Bottom);
        }

        // Reserve the hint's width first, so the label and hint can never overlap.
        int labelRight = content.Right;
        if (hint is not null && (selected || hovered))
        {
            var hintFont = WindowTheme.Mono(7.5f * fontScale);
            int hintWidth = TextRenderer.MeasureText(g, hint, hintFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            var hintBox = Rectangle.FromLTRB(content.Right - hintWidth, content.Top, content.Right, content.Bottom);
            TextRenderer.DrawText(g, hint, hintFont, hintBox, selected ? WindowTheme.AccentSoftText : WindowTheme.Faint,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            labelRight = hintBox.Left - Px(8);
            layout.HintWidth = hintWidth;
        }
        var labelFont = selected ? WindowTheme.Sans(9.5f * fontScale, FontStyle.Bold) : WindowTheme.Sans(9.5f * fontScale);
        layout.LabelWidth = TextRenderer.MeasureText(g, text, labelFont, Size.Empty, TextFormatFlags.NoPadding).Width;
        layout.LabelSpace = labelRight - content.Left;
        TextRenderer.DrawText(g, text, labelFont, Rectangle.FromLTRB(content.Left, content.Top, labelRight, content.Bottom),
            selected ? WindowTheme.AccentSoftText : hovered ? WindowTheme.Body : WindowTheme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        return layout;
    }

    /// <summary>Measured widths, so the diagnostic can prove nothing is clipped.</summary>
    internal sealed class Layout
    {
        public int LabelWidth, LabelSpace, HintWidth;
        public bool Fits => LabelWidth <= LabelSpace;
    }
}

/// <summary>Small outline icons for the sidebar, drawn as vectors so they never clip.</summary>
internal static class LineIcons
{
    public static void Draw(Graphics g, string name, Rectangle box, Color color, int dpi)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.6f * dpi / 96) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float x = box.Left, y = box.Top, w = box.Width;
        PointF P(float px, float py) => new(x + px * w, y + py * w);
        RectangleF R(float left, float top, float right, float bottom) =>
            RectangleF.FromLTRB(x + left * w, y + top * w, x + right * w, y + bottom * w);
        switch (name)
        {
            case "search":
                g.DrawEllipse(pen, R(0.08f, 0.08f, 0.68f, 0.68f));
                g.DrawLine(pen, P(0.6f, 0.6f), P(0.92f, 0.92f));
                break;
            case "trades":
                g.DrawLine(pen, P(0.08f, 0.32f), P(0.88f, 0.32f));
                g.DrawLines(pen, [P(0.68f, 0.12f), P(0.88f, 0.32f), P(0.68f, 0.52f)]);
                g.DrawLine(pen, P(0.92f, 0.7f), P(0.12f, 0.7f));
                g.DrawLines(pen, [P(0.32f, 0.5f), P(0.12f, 0.7f), P(0.32f, 0.9f)]);
                break;
            case "currency":
                g.DrawEllipse(pen, R(0.06f, 0.06f, 0.94f, 0.94f));
                g.DrawEllipse(pen, R(0.3f, 0.3f, 0.7f, 0.7f));
                break;
            case "settings":
                g.DrawEllipse(pen, R(0.36f, 0.36f, 0.64f, 0.64f));
                for (int tooth = 0; tooth < 8; tooth++)
                {
                    double angle = tooth * Math.PI / 4;
                    float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
                    g.DrawLine(pen, P(0.5f + cos * 0.3f, 0.5f + sin * 0.3f), P(0.5f + cos * 0.44f, 0.5f + sin * 0.44f));
                }
                g.DrawEllipse(pen, R(0.2f, 0.2f, 0.8f, 0.8f));
                break;
            case "about":
                g.DrawEllipse(pen, R(0.06f, 0.06f, 0.94f, 0.94f));
                g.DrawLine(pen, P(0.5f, 0.46f), P(0.5f, 0.72f));
                using (var dot = new SolidBrush(color))
                {
                    float r = 0.07f * w;
                    g.FillEllipse(dot, x + 0.5f * w - r, y + 0.3f * w - r, r * 2, r * 2);
                }
                break;
        }
    }
}

/// <summary>A macOS-style switch.</summary>
internal sealed class Toggle : CheckBox
{
    public Toggle()
    {
        AutoSize = false;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        Size = new Size(40, 22);
        Margin = new Padding(0);
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        Size = new Size(this.Px(40), this.Px(22));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? WindowTheme.Card);
        var track = new Rectangle(0, 0, Width - 1, Height - 1);
        WindowTheme.FillRounded(g, track, track.Height / 2,
            Checked ? WindowTheme.Action : WindowTheme.Sunken,
            Checked ? WindowTheme.ActionEdge : (Focused && ShowFocusCues ? WindowTheme.AccentText : WindowTheme.BorderStrong));
        int knob = Height - this.Px(6);
        int x = Checked ? Width - knob - this.Px(3) : this.Px(3);
        using var brush = new SolidBrush(Checked ? WindowTheme.ActionText : WindowTheme.Muted);
        g.FillEllipse(brush, x, this.Px(3), knob - 1, knob - 1);
    }
}

/// <summary>A single-line input with a rounded, sunken frame.</summary>
internal sealed class TextField : Panel
{
    public TextBox Input { get; } = new()
    {
        BorderStyle = BorderStyle.None,
        BackColor = WindowTheme.Sunken,
        ForeColor = WindowTheme.Foreground,
        Dock = DockStyle.Fill,
    };

    public TextField(string? placeholder = null)
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = WindowTheme.Sunken;
        Padding = new Padding(10, 8, 10, 6);
        Height = 34;
        Input.Font = WindowTheme.Sans(10f);
        if (placeholder is not null) Input.PlaceholderText = placeholder;
        Input.GotFocus += (_, _) => Invalidate();
        Input.LostFocus += (_, _) => Invalidate();
        Controls.Add(Input);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? WindowTheme.Card);
        WindowTheme.FillRounded(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), this.Px(7),
            WindowTheme.Sunken, Input.Focused ? WindowTheme.AccentBorder : WindowTheme.BorderStrong);
    }
}

/// <summary>The ExileWorth mark: the Vaal-orb silhouette from the macOS app, drawn natively.</summary>
internal sealed class BrandMark : Control
{
    // The mark is the brand, so it stays Vaal red whatever accent is chosen.
    private static readonly Color MarkLight = Color.FromArgb(207, 31, 45);
    private static readonly Color MarkMiddle = Color.FromArgb(158, 17, 34);
    private static readonly Color MarkDark = Color.FromArgb(76, 8, 18);

    public BrandMark(int size)
    {
        DoubleBuffered = true;
        Size = new Size(size, size);
        Margin = new Padding(0);
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "ExileWorth";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? WindowTheme.Background);
        Draw(e.Graphics, ClientRectangle, Parent?.BackColor ?? WindowTheme.Background);
    }

    public static void Draw(Graphics g, Rectangle bounds, Color on)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Math.Min(bounds.Width, bounds.Height) / 64f;
        PointF P(float x, float y) => new(bounds.Left + x * s, bounds.Top + y * s);
        using var body = new GraphicsPath();
        body.AddBeziers([
            P(32, 5), P(46, 5), P(55, 13), P(55, 26), P(55, 39), P(47, 49), P(40, 55),
            P(36, 58.5f), P(33.5f, 61), P(32, 61), P(30.5f, 61), P(28, 58.5f), P(24, 55),
            P(17, 49), P(9, 39), P(9, 26), P(9, 13), P(18, 5), P(32, 5),
        ]);
        body.CloseFigure();
        using var gradient = new LinearGradientBrush(P(13, 5), P(51, 61), MarkLight, MarkDark);
        gradient.InterpolationColors = new ColorBlend
        {
            Colors = [MarkLight, MarkMiddle, MarkDark],
            Positions = [0f, 0.58f, 1f],
        };
        g.FillPath(gradient, body);
        using var cut = new SolidBrush(on);
        g.FillPolygon(cut, [P(15.5f, 21.5f), P(29.5f, 26), P(26, 36), P(13.5f, 30.5f)]);
        g.FillPolygon(cut, [P(48.5f, 21.5f), P(34.5f, 26), P(38, 36), P(50.5f, 30.5f)]);
        using var mouth = new Pen(on, 6.5f * s) { LineJoin = LineJoin.Miter };
        g.DrawLines(mouth, [P(25, 42), P(32, 53), P(39, 42)]);
    }
}

/// <summary>
/// A painted, scrolling list. One control instead of dozens of children keeps
/// the overlay cheap to redraw and free of focusable widgets.
/// </summary>
internal abstract class ScrollList : Control
{
    private int offset, hover = -1;
    private bool dragging;
    private int dragStart, dragOffset;
    private string placeholder = "";

    protected ScrollList()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = WindowTheme.Card;
        TabStop = false;
    }

    public event Action<int, Point>? RowClicked;

    public string Placeholder
    {
        get => placeholder;
        set { placeholder = value; Invalidate(); }
    }

    protected abstract int Count { get; }
    protected abstract int RowHeight { get; }
    /// <summary>Rows may grow, for example to show a whisper's prepared replies.</summary>
    protected virtual int HeightOf(int index) => RowHeight;
    protected virtual int HeaderHeight => 0;
    protected int HoverIndex => hover;
    protected abstract void DrawRow(Graphics g, int index, Rectangle bounds, bool hovered);
    protected virtual void DrawHeader(Graphics g, Rectangle bounds) { }

    /// <summary>Total height of every row, used to size compact lists to their content.</summary>
    public int ContentHeight
    {
        get
        {
            int total = 0;
            for (int index = 0; index < Count; index++) total += HeightOf(index);
            return total;
        }
    }

    private int ViewHeight => Math.Max(0, Height - HeaderHeight);
    private int MaxOffset => Math.Max(0, ContentHeight - ViewHeight);

    private int TopOf(int index)
    {
        int top = 0;
        for (int row = 0; row < index; row++) top += HeightOf(row);
        return HeaderHeight + top - offset;
    }

    protected void Reset()
    {
        offset = 0;
        hover = -1;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int header = HeaderHeight;
        if (header > 0) DrawHeader(g, new Rectangle(0, 0, Width, header));
        if (Count == 0)
        {
            if (placeholder.Length > 0)
                TextRenderer.DrawText(g, placeholder, WindowTheme.Sans(9.5f),
                    Rectangle.FromLTRB(this.Px(8), header, Width - this.Px(8), Height), WindowTheme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return;
        }
        offset = Math.Clamp(offset, 0, MaxOffset);
        g.SetClip(new Rectangle(0, header, Width, ViewHeight));
        int top = header - offset;
        for (int index = 0; index < Count && top <= Height; index++)
        {
            int height = HeightOf(index);
            if (top + height >= header)
                DrawRow(g, index, new Rectangle(0, top, Width - this.Px(8), height), index == hover);
            top += height;
        }
        g.ResetClip();
        if (MaxOffset > 0)
        {
            var thumb = Thumb();
            WindowTheme.FillRounded(g, thumb, thumb.Width / 2, dragging ? WindowTheme.Muted : WindowTheme.BorderStrong);
        }
    }

    private Rectangle Thumb()
    {
        int track = ViewHeight;
        int length = Math.Max(this.Px(24), track * track / Math.Max(1, ContentHeight));
        int top = HeaderHeight + (track - length) * offset / Math.Max(1, MaxOffset);
        return new Rectangle(Width - this.Px(5), top, this.Px(4), length);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Scroll(-e.Delta * RowHeight * 2 / 120);
    }

    private void Scroll(int by)
    {
        int next = Math.Clamp(offset + by, 0, MaxOffset);
        if (next == offset) return;
        offset = next;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (MaxOffset > 0 && e.X >= Width - this.Px(10))
        {
            dragging = true;
            dragStart = e.Y;
            dragOffset = offset;
            Capture = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (dragging)
        {
            dragging = false;
            Capture = false;
            Invalidate();
            return;
        }
        int index = IndexAt(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0)
            RowClicked?.Invoke(index, new Point(e.X, e.Y - TopOf(index)));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging)
        {
            int track = ViewHeight - Thumb().Height;
            if (track > 0) { offset = Math.Clamp(dragOffset + (e.Y - dragStart) * MaxOffset / track, 0, MaxOffset); Invalidate(); }
            return;
        }
        int index = IndexAt(e.Location);
        if (index != hover) { hover = index; Invalidate(); }
        Cursor = index >= 0 && IsClickable(index, new Point(e.X, e.Y - TopOf(index))) ? Cursors.Hand : Cursors.Default;
    }

    protected virtual bool IsClickable(int index, Point local) => false;

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hover != -1) { hover = -1; Invalidate(); }
    }

    private int IndexAt(Point point)
    {
        if (point.Y < HeaderHeight || point.X >= Width - this.Px(8)) return -1;
        int top = HeaderHeight - offset;
        for (int index = 0; index < Count; index++)
        {
            int height = HeightOf(index);
            if (point.Y < top + height) return point.Y >= top ? index : -1;
            top += height;
        }
        return -1;
    }
}

/// <summary>Trade listings: price with its currency icon, presence, account and age.</summary>
internal sealed class ListingView : ScrollList
{
    private IReadOnlyList<Listing> listings = [];
    private readonly bool compact;

    public ListingView(bool compact)
    {
        this.compact = compact;
        AccessibleName = "Trade listings";
        AccessibleRole = AccessibleRole.Table;
        CurrencyIcons.Changed += Invalidate;
    }

    public bool ShowLevel { get; init; } = true;

    public IReadOnlyList<Listing> Listings
    {
        get => listings;
        set { listings = value; Reset(); }
    }

    protected override int Count => listings.Count;
    protected override int RowHeight => this.Px(compact ? 24 : 30);
    protected override int HeaderHeight => this.Px(compact ? 22 : 26);

    private (Rectangle price, Rectangle level, Rectangle account, Rectangle age) Columns(Rectangle row)
    {
        int pad = this.Px(compact ? 4 : 10);
        int priceWidth = this.Px(compact ? 92 : 112);
        int levelWidth = ShowLevel ? this.Px(42) : 0;
        int ageWidth = this.Px(46);
        var price = new Rectangle(row.Left + pad, row.Top, priceWidth, row.Height);
        var level = new Rectangle(price.Right, row.Top, levelWidth, row.Height);
        var age = new Rectangle(row.Right - pad - ageWidth, row.Top, ageWidth, row.Height);
        var account = Rectangle.FromLTRB(level.Right + this.Px(12), row.Top, age.Left - this.Px(6), row.Bottom);
        return (price, level, account, age);
    }

    protected override void DrawHeader(Graphics g, Rectangle bounds)
    {
        var (price, level, account, age) = Columns(new Rectangle(0, 0, Width - this.Px(8), bounds.Height));
        var font = WindowTheme.Mono(7f);
        var flags = TextFormatFlags.VerticalCenter;
        WindowTheme.Text(g, "PRICE", font, price, WindowTheme.Faint, flags);
        if (ShowLevel) WindowTheme.Text(g, "ILVL", font, level, WindowTheme.Faint, flags | TextFormatFlags.Right);
        WindowTheme.Text(g, "ACCOUNT", font, Rectangle.FromLTRB(account.Left + this.Px(11), account.Top, account.Right, account.Bottom), WindowTheme.Faint, flags);
        WindowTheme.Text(g, "LISTED", font, age, WindowTheme.Faint, flags | TextFormatFlags.Right);
        using var line = new Pen(WindowTheme.Hairline);
        g.DrawLine(line, 0, bounds.Bottom - 1, Width - this.Px(8), bounds.Bottom - 1);
    }

    protected override void DrawRow(Graphics g, int index, Rectangle bounds, bool hovered)
    {
        var listing = listings[index];
        if (hovered && listing.Whisper is not null)
            WindowTheme.FillRounded(g, Rectangle.Inflate(bounds, 0, -1), this.Px(5), WindowTheme.Hover);
        var (price, level, account, age) = Columns(bounds);

        int icon = this.Px(compact ? 16 : 18);
        int x = price.Left;
        if (CurrencyIcons.Get(listing.Currency) is { } image)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, new Rectangle(x, bounds.Top + (bounds.Height - icon) / 2, icon, icon));
            x += icon + this.Px(5);
        }
        string amount = WindowTheme.Amount(listing.Amount);
        var amountFont = WindowTheme.Mono(compact ? 9f : 9.5f, FontStyle.Bold);
        int amountWidth = TextRenderer.MeasureText(g, amount, amountFont, Size.Empty, TextFormatFlags.NoPadding).Width;
        // Drawn at its natural width: a clipped box turned some prices into "…".
        int amountHeight = TextRenderer.MeasureText(g, amount, amountFont, Size.Empty, TextFormatFlags.NoPadding).Height;
        TextRenderer.DrawText(g, amount, amountFont, new Point(x, bounds.Top + (bounds.Height - amountHeight) / 2),
            WindowTheme.Foreground, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        WindowTheme.Text(g, WindowTheme.ShortCurrency(listing.Currency), WindowTheme.Sans(compact ? 7.5f : 8f),
            Rectangle.FromLTRB(x + amountWidth + this.Px(3), bounds.Top, price.Right, bounds.Bottom), WindowTheme.Dim);

        if (ShowLevel)
            WindowTheme.Text(g, listing.Ilvl?.ToString() ?? "—", WindowTheme.Mono(8.5f), level, WindowTheme.Secondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);

        int dot = this.Px(6);
        using (var presence = new SolidBrush(WindowTheme.PresenceColor(listing.Presence)))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillEllipse(presence, account.Left, bounds.Top + (bounds.Height - dot) / 2, dot, dot);
        }
        WindowTheme.Text(g, listing.AccountName, WindowTheme.Sans(compact ? 8.5f : 9f),
            Rectangle.FromLTRB(account.Left + this.Px(11), bounds.Top, account.Right, bounds.Bottom),
            listing.Presence == "offline" ? WindowTheme.Muted : WindowTheme.Body);
        WindowTheme.Text(g, Age(listing.Indexed), WindowTheme.Mono(8f), age, WindowTheme.Dim,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right);

        if (!compact && index < listings.Count - 1)
        {
            using var line = new Pen(WindowTheme.Hairline);
            g.DrawLine(line, bounds.Left + this.Px(8), bounds.Bottom - 1, bounds.Right - this.Px(8), bounds.Bottom - 1);
        }
    }

    protected override bool IsClickable(int index, Point local) => listings[index].Whisper is not null;

    public static string Age(string? indexed)
    {
        if (!DateTimeOffset.TryParse(indexed, out var then)) return "";
        double minutes = Math.Max(0, (DateTimeOffset.UtcNow - then).TotalMinutes);
        if (minutes < 60) return $"{Math.Round(minutes)}m";
        double hours = Math.Round(minutes / 60);
        if (hours < 24) return $"{hours}h";
        double days = Math.Round(hours / 24);
        if (days < 30) return $"{days}d";
        double months = Math.Round(days / 30);
        return months < 12 ? $"{months}mo" : $"{Math.Round(months / 12)}y";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) CurrencyIcons.Changed -= Invalidate;
        base.Dispose(disposing);
    }
}

/// <summary>Item modifiers as compact chips with a checkbox, like the macOS panel.</summary>
internal sealed class ModifierView : ScrollList
{
    private readonly List<(Modifier Modifier, bool Enabled)> items = [];
    private readonly bool compact;

    public ModifierView(bool compact = false)
    {
        this.compact = compact;
        AccessibleName = "Item modifiers";
        AccessibleRole = AccessibleRole.List;
        RowClicked += (index, _) =>
        {
            items[index] = (items[index].Modifier, !items[index].Enabled);
            Invalidate();
            Changed?.Invoke();
        };
    }

    public event Action? Changed;

    public void SetItems(IEnumerable<Modifier> modifiers)
    {
        items.Clear();
        items.AddRange(modifiers.Select(modifier => (modifier, modifier.Enabled)));
        Reset();
    }

    public string[] Selected => items.Where(item => item.Enabled).Select(item => item.Modifier.Key).ToArray();
    public int ItemCount => items.Count;

    protected override int Count => items.Count;
    protected override int RowHeight => this.Px(compact ? 24 : 30);

    protected override bool IsClickable(int index, Point local) => true;

    protected override void DrawRow(Graphics g, int index, Rectangle bounds, bool hovered)
    {
        var (modifier, enabled) = items[index];
        var chip = Rectangle.FromLTRB(bounds.Left, bounds.Top + this.Px(1), bounds.Right, bounds.Bottom - this.Px(1));
        WindowTheme.FillRounded(g, chip, this.Px(6), hovered ? WindowTheme.Hover : WindowTheme.Chip, WindowTheme.Hairline);
        int side = this.Px(compact ? 12 : 14);
        var box = new Rectangle(chip.Left + this.Px(10), chip.Top + (chip.Height - side) / 2, side, side);
        if (enabled)
        {
            WindowTheme.FillRounded(g, box, this.Px(4), WindowTheme.Action);
            using var tick = new Pen(WindowTheme.ActionText, 1.6f * DeviceDpi / 96);
            g.DrawLines(tick, [
                new Point(box.Left + side / 4, box.Top + side / 2),
                new Point(box.Left + side * 2 / 5, box.Top + side * 7 / 10),
                new Point(box.Left + side * 3 / 4, box.Top + side * 3 / 10),
            ]);
        }
        else WindowTheme.FillRounded(g, box, this.Px(4), WindowTheme.Sunken, WindowTheme.BorderStrong);
        WindowTheme.Text(g, modifier.Label, WindowTheme.Sans(compact ? 8.5f : 9f),
            Rectangle.FromLTRB(box.Right + this.Px(10), chip.Top, chip.Right - this.Px(8), chip.Bottom),
            enabled ? WindowTheme.Body : WindowTheme.Muted);
    }
}

/// <summary>
/// Recent trade whispers with the macOS actions. Every button is one game
/// command; Reply opens the prepared replies and Done only dims the row.
/// </summary>
internal sealed class TradeView : ScrollList
{
    private static readonly string[] Actions = ["Invite", "Trade", "Thanks", "Kick", "Reply", "Done"];

    private IReadOnlyList<TradeWhisper> trades = [];
    private readonly HashSet<string> done = [];
    private string? expanded;
    private readonly bool compact;

    public TradeView(bool compact)
    {
        this.compact = compact;
        AccessibleName = "Trade requests";
        AccessibleRole = AccessibleRole.List;
        RowClicked += (index, local) => Activate(trades[index], local);
    }

    /// <summary>Raised with the whisper, the chat command, and a description of it.</summary>
    public event Action<TradeWhisper, string, string>? CommandChosen;

    public IReadOnlyList<TradeWhisper> Trades
    {
        get => trades;
        set
        {
            trades = value;
            if (expanded is not null && !value.Any(trade => Key(trade) == expanded)) expanded = null;
            Reset();
        }
    }

    private static string Key(TradeWhisper trade) => $"{trade.ReceivedAt}|{trade.Character}|{trade.Raw}";
    private bool IsExpanded(TradeWhisper trade) => expanded == Key(trade);

    protected override int Count => trades.Count;
    protected override int RowHeight => this.Px(compact ? 72 : 80);
    private int ReplyHeight => this.Px(compact ? 24 : 26);

    protected override int HeightOf(int index)
    {
        var trade = trades[index];
        return RowHeight + (IsExpanded(trade) ? (trade.Replies?.Length ?? 0) * ReplyHeight + this.Px(4) : 0);
    }

    private Rectangle ActionBounds(int action)
    {
        int pad = this.Px(10), gap = this.Px(4), height = this.Px(22);
        int available = Width - this.Px(8) - pad * 2 - gap * (Actions.Length - 1);
        int chip = Math.Min(this.Px(72), available / Actions.Length);
        return new Rectangle(pad + action * (chip + gap), RowHeight - height - this.Px(10), chip, height);
    }

    private Rectangle ReplyBounds(int reply) =>
        new(this.Px(10), RowHeight - this.Px(4) + reply * ReplyHeight, Width - this.Px(8) - this.Px(20), ReplyHeight - this.Px(3));

    private void Activate(TradeWhisper trade, Point local)
    {
        for (int action = 0; action < Actions.Length; action++)
        {
            if (!ActionBounds(action).Contains(local)) continue;
            switch (Actions[action])
            {
                case "Invite": CommandChosen?.Invoke(trade, trade.Invite, $"Invite {trade.Character}"); break;
                case "Trade": CommandChosen?.Invoke(trade, trade.Trade, $"Trade with {trade.Character}"); break;
                case "Thanks": CommandChosen?.Invoke(trade, trade.Reply, $"Thanks to {trade.Character}"); break;
                case "Kick": CommandChosen?.Invoke(trade, trade.Kick, $"Kick {trade.Character}"); break;
                case "Reply": expanded = IsExpanded(trade) ? null : Key(trade); break;
                case "Done": if (!done.Add(Key(trade))) done.Remove(Key(trade)); break;
            }
            Invalidate();
            return;
        }
        if (!IsExpanded(trade)) return;
        var replies = trade.Replies ?? [];
        for (int reply = 0; reply < replies.Length; reply++)
            if (ReplyBounds(reply).Contains(local))
                CommandChosen?.Invoke(trade, replies[reply], $"Reply to {trade.Character}");
    }

    protected override bool IsClickable(int index, Point local) =>
        Enumerable.Range(0, Actions.Length).Any(action => ActionBounds(action).Contains(local)) ||
        (IsExpanded(trades[index]) && Enumerable.Range(0, trades[index].Replies?.Length ?? 0).Any(reply => ReplyBounds(reply).Contains(local)));

    protected override void DrawRow(Graphics g, int index, Rectangle bounds, bool hovered)
    {
        var trade = trades[index];
        bool finished = done.Contains(Key(trade));
        var card = Rectangle.FromLTRB(bounds.Left, bounds.Top + this.Px(3), bounds.Right, bounds.Bottom - this.Px(3));
        WindowTheme.FillRounded(g, card, this.Px(8), finished ? WindowTheme.Card : WindowTheme.Chip,
            hovered && !finished ? WindowTheme.BorderStrong : WindowTheme.Hairline);
        int pad = this.Px(10);
        var head = new Rectangle(card.Left + pad, card.Top + this.Px(5), card.Width - pad * 2, this.Px(18));
        bool incoming = trade.Direction == "incoming";
        string age = ListingView.Age(trade.ReceivedAt);
        WindowTheme.Text(g, (incoming ? "" : "To ") + trade.Character, WindowTheme.Sans(9.5f, FontStyle.Bold), head,
            finished ? WindowTheme.Faint : WindowTheme.Foreground);
        WindowTheme.Text(g, (finished ? "DONE · " : incoming ? "WTB · " : "SENT · ") + age, WindowTheme.Mono(7f), head,
            incoming && !finished ? WindowTheme.UpText : WindowTheme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        var summary = new Rectangle(head.Left, head.Bottom, head.Width, this.Px(17));
        WindowTheme.Text(g, trade.Summary, WindowTheme.Sans(8.5f), summary, finished ? WindowTheme.Faint : WindowTheme.AccentText);

        var local = PointToClient(Cursor.Position);
        local.Offset(-bounds.Left, -bounds.Top);
        for (int action = 0; action < Actions.Length; action++)
        {
            var area = ActionBounds(action);
            bool active = Actions[action] switch { "Reply" => IsExpanded(trade), "Done" => finished, _ => false };
            string label = Actions[action] == "Done" && finished ? "Undo" : Actions[action];
            Chip(g, area, bounds, label, hovered && area.Contains(local), active, finished && !active);
        }
        if (!IsExpanded(trade)) return;
        var replies = trade.Replies ?? [];
        for (int reply = 0; reply < replies.Length; reply++)
        {
            var area = ReplyBounds(reply);
            string text = replies[reply];
            int space = text.IndexOf(' ');
            Chip(g, area, bounds, "↪  " + (space > 0 ? text[(space + 1)..] : text), hovered && area.Contains(local), false, false,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    private void Chip(Graphics g, Rectangle area, Rectangle row, string label, bool over, bool active, bool dim,
        TextFormatFlags align = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter)
    {
        area.Offset(row.Left, row.Top);
        WindowTheme.FillRounded(g, area, this.Px(5),
            over || active ? WindowTheme.AccentFill : WindowTheme.Raised,
            over || active ? WindowTheme.AccentBorder : WindowTheme.Border);
        var text = align.HasFlag(TextFormatFlags.HorizontalCenter) ? area
            : Rectangle.FromLTRB(area.Left + this.Px(8), area.Top, area.Right - this.Px(6), area.Bottom);
        WindowTheme.Text(g, label, WindowTheme.Sans(8f), text,
            over || active ? WindowTheme.AccentSoftText : dim ? WindowTheme.Faint : WindowTheme.Body, align);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (HoverIndex >= 0) Invalidate();
    }
}

/// <summary>
/// The overlay's currency dropdown, painted inside the overlay itself: a
/// separate popup window could take focus from the game.
/// </summary>
internal sealed class CurrencyPicker : Control
{
    private int hover = -1;
    private string? selected;

    public CurrencyPicker()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Visible = false;
        TabStop = false;
        AccessibleRole = AccessibleRole.MenuPopup;
        AccessibleName = "Listing currency";
        CurrencyIcons.Changed += Invalidate;
    }

    public event Action<string?>? Chosen;

    private int RowHeight => this.Px(28);

    public void Toggle(Point at, string? current)
    {
        if (Visible) { Visible = false; return; }
        selected = current;
        int pad = this.Px(4);
        Bounds = new Rectangle(at, new Size(this.Px(176), GamePriceOverlay.CurrencyFilters.Length * RowHeight + pad * 2));
        Visible = true;
        BringToFront();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? WindowTheme.Overlay);
        WindowTheme.FillRounded(g, new Rectangle(0, 0, Width - 1, Height - 1), this.Px(8), WindowTheme.Raised, WindowTheme.BorderStrong);
        int pad = this.Px(4);
        for (int index = 0; index < GamePriceOverlay.CurrencyFilters.Length; index++)
        {
            var (label, id, _) = GamePriceOverlay.CurrencyFilters[index];
            var row = new Rectangle(pad, pad + index * RowHeight, Width - pad * 2, RowHeight);
            bool active = id == selected;
            if (index == hover || active)
                WindowTheme.FillRounded(g, row, this.Px(5), index == hover ? WindowTheme.AccentFill : WindowTheme.Hover);
            int icon = this.Px(18);
            var iconBox = new Rectangle(row.Left + this.Px(8), row.Top + (row.Height - icon) / 2, icon, icon);
            if (id is not null && CurrencyIcons.Get(id) is { } image)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, iconBox);
            }
            else if (id is null)
                WindowTheme.Text(g, "∗", WindowTheme.Sans(10f), iconBox, WindowTheme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            var text = Rectangle.FromLTRB(iconBox.Right + this.Px(8), row.Top, row.Right - this.Px(8), row.Bottom);
            WindowTheme.Text(g, label, WindowTheme.Sans(9f), text, active ? WindowTheme.AccentSoftText : WindowTheme.Body);
            if (active) WindowTheme.Text(g, "✓", WindowTheme.Sans(9f), text, WindowTheme.AccentText, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }

    private int IndexAt(Point point)
    {
        int index = (point.Y - this.Px(4)) / RowHeight;
        return point.Y >= this.Px(4) && index < GamePriceOverlay.CurrencyFilters.Length ? index : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexAt(e.Location);
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        if (index != hover) { hover = index; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = -1;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int index = IndexAt(e.Location);
        if (index < 0) return;
        Visible = false;
        Chosen?.Invoke(GamePriceOverlay.CurrencyFilters[index].Id);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) CurrencyIcons.Changed -= Invalidate;
        base.Dispose(disposing);
    }
}

internal static class Ui
{
    public static Label Label(string text, Font font, Color color, bool wrap = false) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        AutoSize = true,
        MaximumSize = wrap ? new Size(560, 0) : Size.Empty,
        Margin = new Padding(0, 0, 0, 4),
        UseMnemonic = false,
    };

    /// <summary>Uppercase mono label, the section title style of the macOS app.</summary>
    public static Label Eyebrow(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Font = WindowTheme.Mono(7.5f),
        ForeColor = WindowTheme.Faint,
        AutoSize = true,
        Margin = new Padding(2, 6, 0, 8),
        UseMnemonic = false,
    };

    public static FlowLayoutPanel Row(params Control[] children)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        row.Controls.AddRange(children);
        return row;
    }

    public static TableLayoutPanel Stack(int padding = 0)
    {
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            Padding = new Padding(padding),
            Margin = new Padding(0),
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return stack;
    }

    public static T Add<T>(this TableLayoutPanel stack, T control, SizeType size = SizeType.AutoSize, float value = 0)
        where T : Control
    {
        stack.RowStyles.Add(new RowStyle(size, value));
        stack.Controls.Add(control, 0, stack.RowStyles.Count - 1);
        return control;
    }

    /// <summary>A card with a title, an optional hint, and a body.</summary>
    public static Card Section(string title, string? hint, params Control[] body)
    {
        var card = new Card { Dock = DockStyle.Top, AutoSize = true };
        var stack = Stack();
        stack.Dock = DockStyle.Top;
        stack.Add(Label(title, WindowTheme.Sans(10.5f, FontStyle.Bold), WindowTheme.Foreground));
        if (hint is not null)
        {
            var detail = Label(hint, WindowTheme.Sans(9f), WindowTheme.Muted, wrap: true);
            detail.Margin = new Padding(0, 0, 0, 12);
            stack.Add(detail);
        }
        foreach (var control in body) stack.Add(control);
        card.Controls.Add(stack);
        return card;
    }
}

/// <summary>
/// Item name on the left, the typical asking price on the right, with the
/// crimson bloom the macOS panel uses as its one piece of glow.
/// </summary>
internal sealed class PriceHeader : Control
{
    private string title = "ExileWorth";
    private string meta = "";
    private TypicalPrice? typical;
    private int total;
    private readonly bool compact;

    public PriceHeader(bool compact)
    {
        this.compact = compact;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        AccessibleRole = AccessibleRole.StaticText;
        CurrencyIcons.Changed += Invalidate;
    }

    private Font PriceFont => WindowTheme.Mono(compact ? 14f : 20f, FontStyle.Bold);
    private Font DetailFont => WindowTheme.Mono(compact ? 7.5f : 8f);
    private Font TitleFont => compact ? WindowTheme.Display(12f) : WindowTheme.Display(17f);
    private Font MetaFont => WindowTheme.Sans(compact ? 8.5f : 9.5f);

    private static int Line(Font font) =>
        TextRenderer.MeasureText("Ag01", font, Size.Empty, TextFormatFlags.NoPadding).Height;

    /// <summary>
    /// The height both the title block and the price block need at this DPI.
    /// A fixed row height clipped the "typical · listings" line at 125–150 % scaling.
    /// </summary>
    public int MeasuredHeight
    {
        get
        {
            int top = this.Px(compact ? 8 : 6);
            int price = top + Line(PriceFont) + this.Px(4) + Line(DetailFont);
            int text = this.Px(compact ? 8 : 4) + Line(TitleFont) + this.Px(3) + Line(MetaFont);
            return Math.Max(price, text) + this.Px(compact ? 8 : 6);
        }
    }

    public void Show(string title, string meta, TypicalPrice? typical, int total)
    {
        this.title = title;
        this.meta = meta;
        this.typical = typical;
        this.total = total;
        AccessibleName = typical is null ? title
            : $"{title}, typical price {WindowTheme.Amount(typical.Amount)} {typical.Currency}";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (typical is not null)
        {
            // Keep the whole ellipse above the bottom edge so the bloom fades out
            // instead of being cut off into a visible box.
            int reach = Height * 8 / 10;
            var glow = new Rectangle(Width - this.Px(compact ? 190 : 260), -reach, this.Px(compact ? 240 : 320), reach * 2);
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddEllipse(glow);
            using var bloom = new System.Drawing.Drawing2D.PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(compact ? 64 : 52, WindowTheme.Glow),
                SurroundColors = [Color.FromArgb(0, BackColor)],
            };
            g.FillEllipse(bloom, glow);
        }

        int pad = this.Px(compact ? 12 : 4);
        int priceWidth = 0;
        if (typical is not null)
        {
            string amount = WindowTheme.Amount(typical.Amount);
            var big = PriceFont;
            var unitFont = WindowTheme.Sans(compact ? 9f : 10.5f);
            string unit = typical.Currency;
            Size amountSize = TextRenderer.MeasureText(g, amount, big, Size.Empty, TextFormatFlags.NoPadding);
            Size unitSize = TextRenderer.MeasureText(g, unit, unitFont, Size.Empty, TextFormatFlags.NoPadding);
            int icon = this.Px(compact ? 20 : 26);
            var image = CurrencyIcons.Get(typical.Currency);
            int iconSpace = image is null ? 0 : icon + this.Px(6);
            priceWidth = iconSpace + amountSize.Width + this.Px(4) + unitSize.Width;
            int right = Width - pad;
            int top = this.Px(compact ? 8 : 6);
            int amountHeight = Line(big);
            int x = right - priceWidth;
            if (image is not null)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, new Rectangle(x, top + (amountSize.Height - icon) / 2, icon, icon));
                x += iconSpace;
            }
            TextRenderer.DrawText(g, amount, big, new Point(x, top), Color.White, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, unit, unitFont,
                new Point(x + amountSize.Width + this.Px(4), top + amountSize.Height - unitSize.Height - this.Px(1)),
                WindowTheme.Muted, TextFormatFlags.NoPadding);
            string detail = $"typical · {total:N0} listing{(total == 1 ? "" : "s")}";
            if (typical.Chaos is { } chaos) detail = $"{WindowTheme.Amount(chaos)} chaos · " + detail;
            var detailFont = DetailFont;
            Size detailSize = TextRenderer.MeasureText(g, detail, detailFont, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, detail, detailFont, new Point(right - detailSize.Width, top + amountHeight + this.Px(4)),
                WindowTheme.UpText, TextFormatFlags.NoPadding);
            priceWidth = Math.Max(priceWidth, detailSize.Width) + this.Px(16);
        }

        var titleFont = TitleFont;
        int titleTop = this.Px(compact ? 8 : 4);
        var titleBox = new Rectangle(pad, titleTop, Math.Max(0, Width - pad * 2 - priceWidth), Line(titleFont) + this.Px(2));
        WindowTheme.Text(g, title, titleFont, titleBox, WindowTheme.Foreground, TextFormatFlags.Top | TextFormatFlags.NoPadding);
        var metaBox = new Rectangle(pad, titleBox.Bottom + this.Px(1), titleBox.Width, Math.Max(0, Height - titleBox.Bottom - this.Px(1)));
        // The compact panel has room for one line; the desktop card may wrap to two.
        TextRenderer.DrawText(g, meta, MetaFont, metaBox, WindowTheme.Secondary,
            (compact ? TextFormatFlags.SingleLine : TextFormatFlags.WordBreak) |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) CurrencyIcons.Changed -= Invalidate;
        base.Dispose(disposing);
    }
}
