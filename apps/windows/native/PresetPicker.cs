namespace ExileWorth.Windows;

/// <summary>An in-panel menu, so choosing a preset never takes keyboard focus from the game.</summary>
internal sealed class PresetPicker : Control
{
    public static readonly int[] Percentages = [0, 5, 10, 20];
    private int hover = -1;
    private int? selected;
    private int RowHeight => this.Px(28);

    public PresetPicker()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Visible = false;
        TabStop = false;
        AccessibleRole = AccessibleRole.MenuPopup;
        AccessibleName = "Search minimum preset";
    }

    public event Action<int>? Chosen;

    public void Toggle(Point at, int? current)
    {
        if (Visible) { Visible = false; return; }
        selected = current;
        hover = -1;
        Bounds = new Rectangle(at, new Size(this.Px(112), Percentages.Length * RowHeight + this.Px(8)));
        Visible = true;
        BringToFront();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? WindowTheme.Overlay);
        WindowTheme.FillRounded(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), this.Px(8), WindowTheme.Raised, WindowTheme.BorderStrong);
        int pad = this.Px(4);
        for (int index = 0; index < Percentages.Length; index++)
        {
            int percent = Percentages[index];
            var row = new Rectangle(pad, pad + index * RowHeight, Width - pad * 2, RowHeight);
            bool active = selected == percent;
            if (index == hover || active)
                WindowTheme.FillRounded(e.Graphics, row, this.Px(5), index == hover ? WindowTheme.AccentFill : WindowTheme.Hover);
            var text = Rectangle.Inflate(row, -this.Px(8), 0);
            WindowTheme.Text(e.Graphics, percent == 0 ? "Exact" : $"−{percent}%", WindowTheme.Sans(9f), text,
                active ? WindowTheme.AccentSoftText : WindowTheme.Body);
            if (active) WindowTheme.Text(e.Graphics, "✓", WindowTheme.Sans(9f), text, WindowTheme.AccentText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }

    private int IndexAt(Point point)
    {
        int pad = this.Px(4);
        if (point.X < pad || point.X >= Width - pad || point.Y < pad) return -1;
        int index = (point.Y - pad) / RowHeight;
        return index < Percentages.Length ? index : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexAt(e.Location);
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        if (hover != index) { hover = index; Invalidate(); }
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
        if (e.Button != MouseButtons.Left || index < 0) return;
        Visible = false;
        Chosen?.Invoke(Percentages[index]);
    }
}
