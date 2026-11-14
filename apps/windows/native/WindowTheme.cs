using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ExileWorth.Windows;

/// <summary>
/// The Vaal Red glass palette shared with the macOS app (packages/ui/src/tokens.ts),
/// flattened onto opaque colours because WinForms cannot blend translucent fills.
/// </summary>
internal static class WindowTheme
{
    // Surfaces, darkest first.
    public static readonly Color Window = Color.FromArgb(12, 10, 11);
    public static readonly Color Rail = Color.FromArgb(18, 14, 16);
    public static readonly Color Background = Color.FromArgb(15, 12, 14);
    public static readonly Color Overlay = Color.FromArgb(19, 14, 16);
    public static readonly Color Card = Color.FromArgb(25, 21, 23);
    public static readonly Color Raised = Color.FromArgb(33, 28, 30);
    public static readonly Color Sunken = Color.FromArgb(10, 8, 9);
    public static readonly Color Chip = Color.FromArgb(28, 24, 26);
    public static readonly Color Hover = Color.FromArgb(40, 33, 36);

    // Lines.
    public static readonly Color Hairline = Color.FromArgb(31, 27, 29);
    public static readonly Color Border = Color.FromArgb(42, 37, 39);
    public static readonly Color BorderStrong = Color.FromArgb(58, 52, 54);
    public static readonly Color RimLight = Color.FromArgb(52, 47, 49);

    // Text.
    public static readonly Color Foreground = Color.FromArgb(253, 249, 247);
    public static readonly Color Body = Color.FromArgb(224, 213, 206);
    public static readonly Color Secondary = Color.FromArgb(193, 182, 174);
    public static readonly Color Muted = Color.FromArgb(162, 152, 144);
    public static readonly Color Dim = Color.FromArgb(143, 133, 126);
    public static readonly Color Faint = Color.FromArgb(95, 90, 84);
    public static readonly Color Disabled = Color.FromArgb(67, 64, 60);

    // Brand. Set by ApplyTheme; Vaal Red is the default, as on macOS.
    public static Color Accent { get; private set; }
    public static Color AccentText { get; private set; }
    public static Color Action { get; private set; }
    public static Color ActionText { get; private set; }
    public static Color ActionHover { get; private set; }
    public static Color ActionEdge { get; private set; }
    public static Color AccentFill { get; private set; }
    public static Color AccentFillHover { get; private set; }
    public static Color AccentBorder { get; private set; }
    public static Color AccentSoftText { get; private set; }
    public static Color Glow { get; private set; }
    public static string ThemeName { get; private set; } = "vaalRed";

    /// <summary>The accent palettes the macOS app has offered (packages/ui tokens history).</summary>
    public static readonly (string Id, string Label, Color Accent, Color AccentText, Color Action, Color ActionText)[] Themes =
    [
        ("vaalRed", "Vaal Red", Color.FromArgb(158, 17, 34), Color.FromArgb(221, 85, 96), Color.FromArgb(184, 21, 39), Color.White),
        ("emberGold", "Ember Gold", Color.FromArgb(217, 178, 95), Color.FromArgb(231, 200, 128), Color.FromArgb(176, 58, 58), Color.White),
        ("coldSteel", "Cold Steel", Color.FromArgb(143, 184, 216), Color.FromArgb(169, 204, 230), Color.FromArgb(69, 132, 179), Color.FromArgb(12, 16, 22)),
        ("verdantRot", "Verdant Rot", Color.FromArgb(158, 200, 114), Color.FromArgb(182, 217, 146), Color.FromArgb(77, 127, 71), Color.White),
        ("vaalViolet", "Vaal Violet", Color.FromArgb(180, 138, 217), Color.FromArgb(201, 168, 230), Color.FromArgb(122, 79, 168), Color.White),
        ("boneAsh", "Bone Ash", Color.FromArgb(216, 211, 200), Color.FromArgb(230, 226, 218), Color.FromArgb(138, 131, 120), Color.FromArgb(15, 15, 16)),
    ];

    /// <summary>Raised after the accent changes so open windows can repaint.</summary>
    public static event Action? ThemeChanged;

    static WindowTheme() => ApplyTheme("vaalRed", notify: false);

    public static void ApplyTheme(string? id, bool notify = true)
    {
        var theme = Themes.FirstOrDefault(entry => entry.Id == id);
        if (theme.Id is null) theme = Themes[0];
        ThemeName = theme.Id;
        Accent = theme.Accent;
        AccentText = theme.AccentText;
        Action = theme.Action;
        ActionText = theme.ActionText;
        ActionHover = Mix(theme.Action, Color.White, 0.12);
        ActionEdge = Mix(theme.Action, Color.White, 0.3);
        // Tints are mixed onto the near-black surface, which WinForms cannot blend for us.
        AccentFill = Mix(Card, theme.Accent, 0.24);
        AccentFillHover = Mix(Card, theme.Accent, 0.32);
        AccentBorder = Mix(Card, theme.Accent, 0.55);
        AccentSoftText = Mix(theme.AccentText, Color.White, 0.7);
        Glow = theme.Id == "vaalRed" ? Color.FromArgb(207, 31, 45) : theme.Accent;
        if (notify) ThemeChanged?.Invoke();
    }

    public static Color Mix(Color from, Color to, double amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));

    /// <summary>A round swatch for theme pickers.</summary>
    public static Bitmap Swatch(Color color, int size)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 1, 1, size - 3, size - 3);
        return bitmap;
    }

    /// <summary>Repaints a window and everything in it after a theme change.</summary>
    public static void Repaint(Control root)
    {
        root.Invalidate(true);
        foreach (Control child in root.Controls) Repaint(child);
    }

    // Price movement and presence avoid red and green, as on macOS.
    public static readonly Color Up = Color.FromArgb(143, 174, 106);
    public static readonly Color UpText = Color.FromArgb(179, 209, 151);
    public static readonly Color Down = Color.FromArgb(192, 134, 79);

    public static readonly string SansFamily = Pick("Segoe UI Variable Text", "Segoe UI");
    public static readonly string DisplayFamily = Pick("Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI");
    public static readonly string MonoFamily = Pick("Cascadia Mono", "Cascadia Code", "Consolas");

    private static readonly Dictionary<(string, float, FontStyle), Font> FontCache = [];

    public static Font Sans(float size, FontStyle style = FontStyle.Regular) => Get(SansFamily, size, style);
    public static Font Display(float size, FontStyle style = FontStyle.Bold) => Get(DisplayFamily, size, style);
    public static Font Mono(float size, FontStyle style = FontStyle.Regular) => Get(MonoFamily, size, style);

    private static Font Get(string family, float size, FontStyle style)
    {
        var key = (family, size, style);
        if (!FontCache.TryGetValue(key, out var font))
            FontCache[key] = font = new Font(family, size, style, GraphicsUnit.Point);
        return font;
    }

    private static string Pick(params string[] names)
    {
        using var installed = new System.Drawing.Text.InstalledFontCollection();
        var available = installed.Families.Select(family => family.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.FirstOrDefault(available.Contains) ?? SystemFonts.MessageBoxFont?.FontFamily.Name ?? "Segoe UI";
    }

    /// <summary>Scales a 96-DPI length to the control's monitor.</summary>
    public static int Px(this Control control, int value) => value * control.DeviceDpi / 96;

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

    public static void FillRounded(Graphics graphics, Rectangle bounds, int radius, Color fill, Color? edge = null)
    {
        if (bounds.Width < 1 || bounds.Height < 1) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Rounded(bounds, radius);
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, shape);
        if (edge is { } line)
        {
            using var pen = new Pen(line);
            graphics.DrawPath(pen, shape);
        }
    }

    public static void Text(Graphics graphics, string text, Font font, Rectangle bounds, Color color,
        TextFormatFlags flags = TextFormatFlags.VerticalCenter) =>
        TextRenderer.DrawText(graphics, text, font, bounds, color,
            flags | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

    public static Color PresenceColor(string presence) => presence.ToLowerInvariant() switch
    {
        "online" => Up,
        "afk" => Down,
        _ => Disabled,
    };

    public static string ShortCurrency(string currency) => currency switch
    {
        "divine" => "div",
        "exalted" => "ex",
        _ => currency,
    };

    public static string Amount(double value) =>
        value >= 100 ? value.ToString("#,0")
        : value >= 10 ? value.ToString("0.#")
        : value.ToString("0.##");

    /// <summary>Dark title bar whose caption matches the window, with Windows 11 rounding.</summary>
    public static void ApplyTitleBar(nint handle, Color caption)
    {
        int enabled = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)); // immersive dark mode
        int rounded = 2;
        _ = DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int)); // round corners
        int color = ColorTranslator.ToWin32(caption);
        _ = DwmSetWindowAttribute(handle, 35, ref color, sizeof(int)); // caption colour (Win11)
        int border = ColorTranslator.ToWin32(Border);
        _ = DwmSetWindowAttribute(handle, 34, ref border, sizeof(int)); // border colour (Win11)
    }

    /// <summary>Rounded corners and a red rim for borderless popups on Windows 11.</summary>
    public static void ApplyPopupFrame(nint handle)
    {
        int rounded = 2;
        _ = DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
        int border = ColorTranslator.ToWin32(AccentBorder);
        _ = DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
    }

    public static ContextMenuStrip Menu() => new()
    {
        Renderer = new DarkMenuRenderer(),
        BackColor = Raised,
        ForeColor = Body,
        ShowImageMargin = false,
        ShowCheckMargin = true,
        Font = Sans(9.5f),
        Padding = new Padding(4),
    };

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}

internal sealed class DarkMenuRenderer() : ToolStripProfessionalRenderer(new DarkMenuColors())
{
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? (e.Item.Selected ? WindowTheme.Foreground : WindowTheme.Body) : WindowTheme.Faint;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var box = e.ImageRectangle;
        using var pen = new Pen(WindowTheme.AccentText, 1.6f);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, [
            new Point(box.Left + box.Width / 5, box.Top + box.Height / 2),
            new Point(box.Left + box.Width * 2 / 5, box.Top + box.Height * 7 / 10),
            new Point(box.Left + box.Width * 4 / 5, box.Top + box.Height * 3 / 10),
        ]);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var bounds = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height - 1);
        WindowTheme.FillRounded(e.Graphics, bounds, 5, WindowTheme.AccentFill);
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => WindowTheme.Raised;
        public override Color MenuBorder => WindowTheme.BorderStrong;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => WindowTheme.AccentFill;
        public override Color ImageMarginGradientBegin => WindowTheme.Raised;
        public override Color ImageMarginGradientMiddle => WindowTheme.Raised;
        public override Color ImageMarginGradientEnd => WindowTheme.Raised;
        public override Color CheckBackground => Color.Transparent;
        public override Color CheckSelectedBackground => Color.Transparent;
        public override Color CheckPressedBackground => Color.Transparent;
        public override Color SeparatorDark => WindowTheme.Border;
        public override Color SeparatorLight => WindowTheme.Border;
    }
}
