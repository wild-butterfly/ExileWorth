namespace Vaaluation.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, "Local\\Vaaluation.Windows", out bool first);
        if (!first)
        {
            MessageBox.Show("Vaaluation is already running. Open it from the system tray.", "Vaaluation");
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new PriceWindow());
    }
}
