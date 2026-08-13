using System.Windows;
using System.Windows.Threading;

namespace AutomationTool;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Without this, an unhandled exception on the main UI thread kills the whole
        // process silently — the window just vanishes with no explanation.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"An unexpected error occurred:\n\n{e.Exception}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"A fatal error occurred:\n\n{e.ExceptionObject}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
