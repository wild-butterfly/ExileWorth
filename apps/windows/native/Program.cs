namespace ExileWorth.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, "Local\\Vaaluation.Windows", out bool first);
        if (!first)
        {
            MessageBox.Show("ExileWorth is already running. Open it from the system tray.", "ExileWorth");
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new PriceWindow());
    }
}
