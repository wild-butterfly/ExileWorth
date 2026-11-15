using System.Diagnostics;

namespace ExileWorth.Windows;

internal static class Program
{
    public static string Version => Application.ProductVersion.Split('+')[0];

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--render-sidebar")
        {
            ApplicationConfiguration.Initialize();
            Environment.ExitCode = SidebarRender.Run(args[1]);
            return;
        }

        using var singleInstance = new Mutex(true, "Local\\Vaaluation.Windows", out bool first);
        if (!first && !ReplaceOlderInstance(singleInstance)) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new PriceWindow());
    }

    /// <summary>
    /// An older copy left running in the tray used to win silently: the new
    /// download exited and the tray reopened the old window, so fixes looked
    /// missing. Name both versions and offer to replace the running copy.
    /// </summary>
    private static bool ReplaceOlderInstance(Mutex singleInstance)
    {
        using var current = Process.GetCurrentProcess();
        var running = Process.GetProcessesByName(current.ProcessName).Where(process => process.Id != current.Id).ToArray();
        try
        {
            string? other = running.Select(VersionOf).FirstOrDefault(version => version is not null);
            if (other is null || other == Version)
            {
                MessageBox.Show("ExileWorth is already running. Open it from the system tray.", "ExileWorth");
                return false;
            }
            var answer = MessageBox.Show(
                $"ExileWorth {other} is still running in the system tray, so ExileWorth {Version} can't start.\n\n" +
                $"Close {other} and start {Version}?", "ExileWorth", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) return false;
            foreach (var process in running)
            {
                try { process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            try { return singleInstance.WaitOne(5000); }
            catch (AbandonedMutexException) { return true; } // The old copy held it when it was closed.
        }
        finally
        {
            foreach (var process in running) process.Dispose();
        }
    }

    private static string? VersionOf(Process process)
    {
        try { return process.MainModule?.FileVersionInfo.ProductVersion?.Split('+')[0]; }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }
}

/// <summary>
/// Diagnostic: draws every sidebar row in its normal, hovered and selected
/// states at 100 %, 125 % and 150 % scale with the same painter the app uses,
/// and reports whether each label fits. Emulated on a 96-DPI bitmap; it does
/// not change Windows display scaling.
/// </summary>
internal static class SidebarRender
{
    private static readonly (string Label, string Glyph)[] Rows =
        [("Price Check", "search"), ("Trades", "trades"), ("Currency", "currency"), ("Settings", "settings")];

    public static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new List<string> { "scale,label,state,labelWidth,labelSpace,hintWidth,fits" };
        bool allFit = true;
        foreach (float scale in new[] { 1f, 1.25f, 1.5f })
        {
            int S(float value) => (int)Math.Round(value * scale);
            int rowWidth = S(214 - 20), rowHeight = S(38), gap = S(3), pad = S(10);
            string[] states = ["normal", "hovered", "selected"];
            using var bitmap = new Bitmap(pad + states.Length * (rowWidth + pad), pad + Rows.Length * (rowHeight + gap) + pad);
            using var g = Graphics.FromImage(bitmap);
            g.Clear(WindowTheme.Rail);
            for (int column = 0; column < states.Length; column++)
            {
                for (int row = 0; row < Rows.Length; row++)
                {
                    var bounds = new Rectangle(pad + column * (rowWidth + pad), pad + row * (rowHeight + gap), rowWidth - 1, rowHeight - 1);
                    var layout = NavRow.Paint(g, bounds, Rows[row].Label, Rows[row].Glyph, $"Ctrl+{row + 1}",
                        selected: column == 2, hovered: column == 1, focused: false, WindowTheme.Rail, scale, scale);
                    allFit &= layout.Fits;
                    report.Add($"{scale * 100:0}%,{Rows[row].Label},{states[column]},{layout.LabelWidth},{layout.LabelSpace},{layout.HintWidth},{layout.Fits}");
                }
            }
            bitmap.Save(Path.Combine(folder, $"sidebar-{scale * 100:0}.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        File.WriteAllLines(Path.Combine(folder, "sidebar-fit.csv"), report);
        return allFit ? 0 : 3;
    }
}
