namespace TGAPeek.App;

static class Program
{
    /// <summary>
    ///  The main entry point. Arguments are image paths; with several, the first is shown and
    ///  Left/Right step through the rest in the order given.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // Applies the high-DPI mode (PerMonitorV2), visual styles and default font
        // configured in the project file. See https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ErrorDialogs.ShowUnexpected(null, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorDialogs.ShowUnexpected(null, e.ExceptionObject as Exception);
        Application.Run(new MainForm(AppSettings.Load(), args));
    }
}
