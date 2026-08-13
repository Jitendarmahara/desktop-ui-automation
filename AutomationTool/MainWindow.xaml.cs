using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace AutomationTool;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowForegroundHelper.BringToFront(this);
    }

    private void OnRecordTestClick(object sender, RoutedEventArgs e) => OpenOnOwnStaThread(() => new RecordTestWindow());

    private void OnRunTestClick(object sender, RoutedEventArgs e) => OpenOnOwnStaThread(() => new RunTestWindow());

    // Both the recorder and the runner make UI Automation calls, which are COM/STA-affine.
    // Running each on its own dedicated thread keeps those calls confined to a single
    // thread and keeps this window responsive while either is open.
    private static void OpenOnOwnStaThread(Func<Window> createWindow)
    {
        var thread = new Thread(() =>
        {
            // This thread has its own Dispatcher, separate from the main window's — without
            // this handler, an exception here has nowhere to go and fails completely
            // silently instead of surfacing to the user.
            Dispatcher.CurrentDispatcher.UnhandledException += (_, args) =>
            {
                MessageBox.Show($"An unexpected error occurred:\n\n{args.Exception}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            try
            {
                createWindow().ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open the window:\n\n{ex}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }
}
