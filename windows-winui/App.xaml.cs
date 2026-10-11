using Microsoft.UI.Xaml;

namespace OpenCleaner;

public partial class App : Application
{
    private Window window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            window = new MainWindow();
            window.Activate();
        }
        catch (Exception ex)
        {
            Log(ex);
            throw;
        }
    }

    /// <summary>Appends startup/crash errors to %LocalAppData%\OpenCleaner\crash.log so they can be shared.</summary>
    private static void Log(Exception ex)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCleaner");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), DateTime.Now + "\n" + ex + "\n\n");
        }
        catch
        {
            // logging must never crash the app
        }
    }
}
