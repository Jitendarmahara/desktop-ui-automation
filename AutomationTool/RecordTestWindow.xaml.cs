using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AutomationTool.Builder;
using AutomationTool.Exceptions;
using AutomationTool.Inspector;
using AutomationTool.Locators;
using AutomationTool.Recording;
using AutomationTool.Replay;
using AutomationTool.Steps;
using AutomationTool.TestModel;

namespace AutomationTool;

public partial class RecordTestWindow : Window
{
    private ElementInspector? _inspector;
    private FlaUI.Core.AutomationElements.Window? _targetWindow;
    private string _targetWindowTitle = string.Empty;
    private List<InspectableElement> _elements = new();
    private readonly List<TestStep> _steps = new();
    private IReadOnlyList<StepOption> _currentOptions = Array.Empty<StepOption>();
    private LiveRecorder? _liveRecorder;
    private bool _pendingCaptureWasLost;

    public RecordTestWindow()
    {
        InitializeComponent();
        WindowForegroundHelper.BringToFront(this);
        PositionTopRight();
        Closed += (_, _) =>
        {
            _liveRecorder?.Dispose();
            _inspector?.Dispose();
        };
    }

    // Docks the HUD to a corner instead of centering it, so it never sits on top of the
    // app being recorded — the whole point of shrinking it down in the first place.
    private void PositionTopRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Top + 16;
    }

    private void OnAttachClick(object sender, RoutedEventArgs e)
    {
        var processName = ProcessNameBox.Text.Trim();
        if (string.IsNullOrEmpty(processName))
        {
            StatusText.Text = "Enter a process name first.";
            return;
        }

        AttachButton.IsEnabled = false;
        StatusText.Text = "Attaching...";

        try
        {
            _liveRecorder?.Dispose();
            _inspector?.Dispose();
            _inspector = new ElementInspector();
            var titleFilter = string.IsNullOrWhiteSpace(TitleFilterBox.Text) ? null : TitleFilterBox.Text.Trim();
            _targetWindow = _inspector.Attach(processName, titleFilter, TimeSpan.FromSeconds(10));

            _liveRecorder = new LiveRecorder(_inspector.Automation, _inspector, _targetWindow, OnActionCaptured, OnPendingCaptureLost);
            _liveRecorder.Start();

            RefreshButton.IsEnabled = true;
            // Enabled here rather than only once a step exists: Finish & Save also needs to be
            // reachable when nothing has finalized into a step yet but a live capture is still
            // pending, since clicking it is what flushes that pending capture (see OnFinishClick).
            FinishButton.IsEnabled = true;
            RefreshElements();

            AttachPanel.Visibility = Visibility.Collapsed;
            RecordingStatusPanel.Visibility = Visibility.Visible;
            // Captured once, here, rather than re-read live at save time: if the target app
            // has since closed, a live property read against its now-dead UIA element throws
            // (observed: COM error 0x80040201), which was surfacing as "Finish & Save shows
            // both dialogs through to completion but the file never actually gets written."
            _targetWindowTitle = _targetWindow.Title;
            TargetNameText.Text = _targetWindowTitle;
            StatusText.Text = "Just use the app normally. Button clicks and assertions need " +
                               "\"Add manual step\" below.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            AttachButton.IsEnabled = true;
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshElements();

    private void OnManualExpanderExpanded(object sender, RoutedEventArgs e) => RefreshElements();

    private void UpdateStepCount()
    {
        StepCountText.Text = _steps.Count == 1 ? "1 step captured" : $"{_steps.Count} steps captured";
        // Forward progress since any earlier lost-capture warning — let "Nothing to save yet."
        // apply normally again for whatever happens next.
        _pendingCaptureWasLost = false;
    }

    private void RefreshElements()
    {
        if (_inspector is null || _targetWindow is null)
        {
            return;
        }

        try
        {
            _elements = _inspector.GetActionableElements(_targetWindow).ToList();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error refreshing: {ex.Message}";
            return;
        }

        ElementsList.ItemsSource = _elements.Select(el => el.ToString()).ToList();
        ActionCombo.ItemsSource = null;
        AddStepButton.IsEnabled = false;
        ValueBox.Visibility = Visibility.Collapsed;
        ValueLabel.Visibility = Visibility.Collapsed;

        StatusText.Text = _elements.Count == 0
            ? "No actionable elements found — try Refresh after the app's UI changes."
            : $"{_elements.Count} element(s) found. Select one, then choose an action.";
    }

    private void OnElementSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ElementsList.SelectedIndex < 0 || ElementsList.SelectedIndex >= _elements.Count)
        {
            ActionCombo.ItemsSource = null;
            AddStepButton.IsEnabled = false;
            return;
        }

        var target = _elements[ElementsList.SelectedIndex];
        _currentOptions = StepOptionCatalog.GetOptionsFor(target.ControlType);
        ActionCombo.ItemsSource = _currentOptions.Select(o => o.Label).ToList();
        ActionCombo.SelectedIndex = _currentOptions.Count > 0 ? 0 : -1;
    }

    private void OnActionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ActionCombo.SelectedIndex < 0 || ActionCombo.SelectedIndex >= _currentOptions.Count)
        {
            AddStepButton.IsEnabled = false;
            return;
        }

        var option = _currentOptions[ActionCombo.SelectedIndex];
        ValueBox.Visibility = option.RequiresValue ? Visibility.Visible : Visibility.Collapsed;
        ValueLabel.Visibility = option.RequiresValue ? Visibility.Visible : Visibility.Collapsed;
        ValueBox.Text = string.Empty;
        AddStepButton.IsEnabled = true;
    }

    private void OnAddStepClick(object sender, RoutedEventArgs e)
    {
        if (ElementsList.SelectedIndex < 0 || ActionCombo.SelectedIndex < 0)
        {
            return;
        }

        var target = _elements[ElementsList.SelectedIndex];
        var option = _currentOptions[ActionCombo.SelectedIndex];

        if (option.RequiresValue && string.IsNullOrEmpty(ValueBox.Text))
        {
            StatusText.Text = "Enter a value first.";
            return;
        }

        try
        {
            var locator = LocatorBuilder.Build(target, _elements);
            var order = _steps.Count + 1;
            var value = option.RequiresValue ? ValueBox.Text : null;

            var step = option.Kind == StepKind.Action
                ? TestStep.ForAction(order, locator, option.Action!.Value, value)
                : TestStep.ForAssertion(order, locator, option.Assertion!.Value, value);

            if (step.Kind == StepKind.Action)
            {
                // Executed live against the real app as it's recorded — this is what lets
                // you reach later screens/tabs to keep adding steps.
                ActionExecutor.Execute(target.Element, step.Action!.Value, step.Value);
            }

            _steps.Add(step);
            StepsList.Items.Add($"{step.Order}. {step.Description}");
            UpdateStepCount();
            FinishButton.IsEnabled = true;
            StatusText.Text = $"Added step {step.Order}. If the app's UI just changed, click Refresh.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not add step: {ex.Message}";
        }
    }

    // Called from LiveRecorder's UIA event callbacks, which fire on a background thread —
    // everything here must be marshaled onto this window's own UI thread first.
    private void OnActionCaptured(Locator locator, ActionType action, string? value)
    {
        Dispatcher.Invoke(() =>
        {
            TestStep step;
            try
            {
                step = TestStep.ForAction(_steps.Count + 1, locator, action, value);
            }
            catch (AutomationToolException)
            {
                return;
            }

            _steps.Add(step);
            StepsList.Items.Add($"{step.Order}. {step.Description} (captured)");
            UpdateStepCount();
            FinishButton.IsEnabled = true;
            StatusText.Text = $"Captured step {step.Order} automatically.";
        });
    }

    // Called when a pending typed edit couldn't be turned into a step at all — most likely the
    // target app closed before the edit was finalized. There's no live element left to build a
    // valid locator from at that point, so the edit genuinely can't be recorded; this at least
    // makes that failure visible instead of the value just silently not showing up anywhere.
    private void OnPendingCaptureLost(string typedValue)
    {
        Dispatcher.Invoke(() =>
        {
            _pendingCaptureWasLost = true;
            StatusText.Text = string.IsNullOrEmpty(typedValue)
                ? "A pending edit was lost — the target app closed before it could be captured."
                : $"A pending edit ('{typedValue}') was lost — the target app closed before it could be captured.";
        });
    }

    private void OnFinishClick(object sender, RoutedEventArgs e)
    {
        if (_targetWindow is null)
        {
            return;
        }

        // Flush before the count check, not after: a still-pending live capture (e.g. text
        // just typed, focus never moved away yet) doesn't count toward _steps until this
        // finalizes it, so checking count first could reject a click that would have had
        // something to save.
        _liveRecorder?.Flush();

        if (_steps.Count == 0)
        {
            // _pendingCaptureWasLost isn't reset here: the loss may have already happened (and
            // already been reported) via a focus-change well before this click, not necessarily
            // during this Flush() call — don't clobber that still-unread, more specific message
            // with a generic "nothing to save" just because nothing new happened *this* click.
            if (!_pendingCaptureWasLost)
            {
                StatusText.Text = "Nothing to save yet.";
            }
            return;
        }

        try
        {
            var nameDialog = new PromptWindow("Test name", "Enter a name for this test:") { Owner = this };
            if (nameDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(nameDialog.ResponseText))
            {
                return;
            }

            var name = nameDialog.ResponseText.Trim();
            var saveDialog = new SaveFileDialog
            {
                Title = "Save test",
                FileName = Sanitize(name) + ".json",
                Filter = "Test files (*.json)|*.json",
                InitialDirectory = PathHelper.ResolveSampleTestsDirectory()
            };

            if (saveDialog.ShowDialog() != true)
            {
                return;
            }

            var testCase = TestCase.Create(name, ProcessNameBox.Text.Trim(), _targetWindowTitle, _steps);
            TestSerializer.Save(testCase, saveDialog.FileName);
            MessageBox.Show(this, $"Saved '{name}' with {_steps.Count} step(s) to:\n{saveDialog.FileName}",
                "Test saved", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error saving: {ex.Message}";
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Replace(' ', '-');
        return string.IsNullOrWhiteSpace(cleaned) ? "test" : cleaned.ToLowerInvariant();
    }
}
