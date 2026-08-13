using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using AutomationTool.Inspector;
using AutomationTool.Replay;
using AutomationTool.Reporting;
using AutomationTool.TestModel;

namespace AutomationTool;

public partial class RunTestWindow : Window
{
    private sealed record TestFileEntry(string Path, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private readonly List<TestFileEntry> _testFiles = new();
    private TestCase? _selectedTestCase;

    public RunTestWindow()
    {
        InitializeComponent();
        WindowForegroundHelper.BringToFront(this);
        LoadTestFilesFromSampleTestsFolder();
    }

    private void LoadTestFilesFromSampleTestsFolder()
    {
        var directory = PathHelper.ResolveSampleTestsDirectory();
        if (!Directory.Exists(directory))
        {
            StatusText.Text = "No sample-tests folder found — use Browse to pick a file.";
            return;
        }

        foreach (var path in Directory.GetFiles(directory, "*.json").OrderBy(p => p))
        {
            AddTestFile(path);
        }

        RefreshList();
    }

    private void AddTestFile(string path)
    {
        if (_testFiles.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _testFiles.Add(new TestFileEntry(path, Path.GetFileName(path)));
    }

    private void RefreshList()
    {
        TestFilesList.ItemsSource = null;
        TestFilesList.ItemsSource = _testFiles;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a test file",
            Filter = "Test files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = PathHelper.ResolveSampleTestsDirectory()
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        AddTestFile(dialog.FileName);
        RefreshList();
        TestFilesList.SelectedItem = _testFiles.First(f => f.Path == dialog.FileName);
    }

    private void OnTestFileSelected(object sender, SelectionChangedEventArgs e)
    {
        _selectedTestCase = null;
        RunButton.IsEnabled = false;
        StepsPreviewList.ItemsSource = null;
        TestNameText.Text = string.Empty;
        TestTargetText.Text = string.Empty;

        if (TestFilesList.SelectedItem is not TestFileEntry entry)
        {
            return;
        }

        try
        {
            var testCase = TestSerializer.Load(entry.Path);
            _selectedTestCase = testCase;

            TestNameText.Text = testCase.Name;
            TestTargetText.Text = string.IsNullOrEmpty(testCase.TargetWindowTitle)
                ? $"Target process: {testCase.TargetProcessName}"
                : $"Target process: {testCase.TargetProcessName}  (window: \"{testCase.TargetWindowTitle}\")";

            StepsPreviewList.ItemsSource = testCase.Steps
                .OrderBy(s => s.Order)
                .Select(s => $"{s.Order}. [{s.Kind}] {s.Description}")
                .ToList();

            RunButton.IsEnabled = true;
            StatusText.Text = $"{testCase.Steps.Count} step(s). Ready to run.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not read this file: {ex.Message}";
        }
    }

    private void OnRunClick(object sender, RoutedEventArgs e)
    {
        if (_selectedTestCase is null)
        {
            return;
        }

        RunButton.IsEnabled = false;
        BrowseButton.IsEnabled = false;
        TestFilesList.IsEnabled = false;
        LogList.Items.Clear();
        StatusText.Text = "Running...";

        var testCase = _selectedTestCase;
        var thread = new Thread(() =>
        {
            Dispatcher.CurrentDispatcher.UnhandledException += (_, args) =>
            {
                MessageBox.Show($"An unexpected error occurred:\n\n{args.Exception}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            RunTest(testCase);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private void RunTest(TestCase testCase)
    {
        try
        {
            using var inspector = new ElementInspector();
            var engine = new ReplayEngine(inspector);
            var results = engine.Run(
                testCase,
                onStepStarting: step => AppendLog($"Running step {step.Order}: {step.Description} ..."),
                onStepCompleted: result => AppendLog(FormatStepResult(result)));

            var passed = results.Count(r => r.Passed);
            AppendLog($"Result: {passed}/{results.Count} steps passed.");
            SetStatus(passed == results.Count ? "All steps passed." : $"{results.Count - passed} step(s) failed.");
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
            SetStatus("Run failed — see log.");
        }
        finally
        {
            Dispatcher.Invoke(() =>
            {
                RunButton.IsEnabled = true;
                BrowseButton.IsEnabled = true;
                TestFilesList.IsEnabled = true;
            });
        }
    }

    private static string FormatStepResult(StepResult result)
    {
        var status = result.Passed ? "PASS" : "FAIL";
        var line = $"[{status}] Step {result.Order}: {result.Description}";
        return result.Passed || string.IsNullOrEmpty(result.FailureReason)
            ? line
            : $"{line}    Reason: {result.FailureReason}";
    }

    private void AppendLog(string message) =>
        Dispatcher.Invoke(() =>
        {
            LogList.Items.Add(message);
            if (LogList.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[^1]);
            }
        });

    private void SetStatus(string status) =>
        Dispatcher.Invoke(() => StatusText.Text = status);
}
