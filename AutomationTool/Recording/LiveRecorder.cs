using System.Threading;
using AutomationTool.Inspector;
using AutomationTool.Locators;
using AutomationTool.Steps;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.UIA3;

namespace AutomationTool.Recording;

/// <summary>
/// Captures actions as the user interacts with the real target app directly — no picker,
/// no "add step" button — by subscribing to UI Automation's own event notifications rather
/// than global input hooks. Assertions are still authored manually; this only captures
/// what the user *does*.
///
/// Two capture mechanisms, chosen per what was empirically observed to actually work on
/// WinForms/.NET Framework in this environment:
///  - Value property-changed events (TypeText) and SelectionItem events (tab switches, via
///    Select) both fire reliably through WinForms' native UIA bridge — real UIA events.
///  - InvokedEvent (Button clicks) and ToggleStatePropertyChanged (CheckBox) do NOT fire —
///    confirmed with both simulated and genuine physical mouse input. Click therefore stays
///    a manual "Add Step" action (still executed live when added, same as before). Toggle
///    does have durable state to compare, so it's captured via lightweight polling instead
///    of an event — a pragmatic, low-risk middle ground that still needs no input hooks.
/// </summary>
public sealed class LiveRecorder : IDisposable
{
    // Textbox controls fire a Value property-changed event on every keystroke. Focus moving
    // away from the field being edited is the real "the user is done with this" signal and
    // is used as the primary trigger (see OnFocusChanged). The poll below exists only as a
    // safety net for the rare case the FocusChanged notification itself is missed — and
    // deliberately checks the field's actual current HasKeyboardFocus state rather than just
    // waiting out a fixed duration: an earlier version used a blind timeout (first 700ms,
    // then 4s), and either way a user pausing mid-field for longer than that timeout — even
    // just from getting momentarily distracted — would have the same edit incorrectly split
    // into two steps. Checking the real, current focus state has no such ceiling: it waits
    // exactly as long as the field is genuinely still focused, however long that is.
    private static readonly TimeSpan FocusPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TogglePollInterval = TimeSpan.FromMilliseconds(400);

    private readonly UIA3Automation _automation;
    private readonly ElementInspector _inspector;
    private readonly Window _window;
    private readonly Action<Locator, ActionType, string?> _onActionCaptured;
    private readonly object _lock = new();
    private readonly Timer _debounceTimer;
    private readonly Timer _togglePollTimer;
    private readonly Dictionary<string, ToggleState> _lastToggleStates = new();

    private AutomationEventHandlerBase? _invokeHandler;
    private AutomationEventHandlerBase? _selectionHandler;
    private PropertyChangedEventHandlerBase? _valueChangedHandler;
    private FocusChangedEventHandlerBase? _focusHandler;

    private PendingTextChange? _pending;
    private bool _started;

    // Deliberately doesn't carry the value from the event args: that payload isn't reliable
    // when the change was made by a different UIA client stack than the one listening
    // (observed empirically — captured TypeText steps ended up with empty values). The
    // current value is re-read directly from the element when finalizing instead.
    private sealed record PendingTextChange(string IdentityKey, AutomationElement Element);

    public LiveRecorder(UIA3Automation automation, ElementInspector inspector, Window window, Action<Locator, ActionType, string?> onActionCaptured)
    {
        _automation = automation ?? throw new ArgumentNullException(nameof(automation));
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _onActionCaptured = onActionCaptured ?? throw new ArgumentNullException(nameof(onActionCaptured));
        _debounceTimer = new Timer(_ => CheckPendingStillFocused(), null, Timeout.Infinite, Timeout.Infinite);
        _togglePollTimer = new Timer(_ => PollToggleStates(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _invokeHandler = _window.RegisterAutomationEvent(
            _automation.EventLibrary.Invoke.InvokedEvent, TreeScope.Subtree, (element, _) => OnInvoked(element));
        _selectionHandler = _window.RegisterAutomationEvent(
            _automation.EventLibrary.SelectionItem.ElementSelectedEvent, TreeScope.Subtree, (element, _) => OnSelected(element));
        _valueChangedHandler = _window.RegisterPropertyChangedEvent(
            TreeScope.Subtree, (element, _, value) => OnValueChanged(element, value), _automation.PropertyLibrary.Value.Value);
        _focusHandler = _automation.RegisterFocusChangedEvent(OnFocusChanged);

        _togglePollTimer.Change(TogglePollInterval, TogglePollInterval);
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        FlushPending();
        _togglePollTimer.Change(Timeout.Infinite, Timeout.Infinite);

        try
        {
            _automation.UnregisterAllEvents();
        }
        catch (Exception)
        {
            // Best-effort cleanup — the window/process may already be gone.
        }

        _debounceTimer.Dispose();
        _togglePollTimer.Dispose();
        _started = false;
    }

    public void Dispose() => Stop();

    /// <summary>Finalizes any in-progress (debounced) text capture immediately, without
    /// stopping recording — used before saving, so a still-pending edit isn't lost.</summary>
    public void Flush() => FlushPending();

    private void OnInvoked(AutomationElement element)
    {
        FlushPending();
        TryCapture(element, ActionType.Click, null);
    }

    private void OnSelected(AutomationElement element)
    {
        FlushPending();
        TryCapture(element, ActionType.Select, null);
    }

    private void OnValueChanged(AutomationElement element, object? value)
    {
        var key = GetIdentityKey(element);
        if (key is null)
        {
            return;
        }

        lock (_lock)
        {
            _pending = new PendingTextChange(key, element);
        }

        // Periodic, not one-shot: keeps re-checking real focus state for as long as there's
        // a pending edit, with no upper bound on how long that can take.
        _debounceTimer.Change(FocusPollInterval, FocusPollInterval);
    }

    // Primary finalize trigger: focus leaving the field being edited is the real signal that
    // the user is done with it, regardless of how long they paused while typing.
    private void OnFocusChanged(AutomationElement newFocus)
    {
        var newKey = GetIdentityKey(newFocus);

        PendingTextChange? toFinalize = null;
        lock (_lock)
        {
            if (_pending is not null && _pending.IdentityKey != newKey)
            {
                toFinalize = _pending;
                _pending = null;
            }
        }

        if (toFinalize is not null)
        {
            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            FinalizeTextChange(toFinalize);
        }
    }

    private void CheckPendingStillFocused()
    {
        PendingTextChange? toFinalize = null;
        lock (_lock)
        {
            if (_pending is not null && !StillHasFocus(_pending.Element))
            {
                toFinalize = _pending;
                _pending = null;
            }
        }

        if (toFinalize is not null)
        {
            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            FinalizeTextChange(toFinalize);
        }
    }

    private static bool StillHasFocus(AutomationElement element)
    {
        try
        {
            return element.Properties.HasKeyboardFocus.Value;
        }
        catch (Exception)
        {
            // Can't tell — assume it might still be focused rather than risk splitting a
            // step the user hasn't actually finished. OnFocusChanged remains the fast,
            // authoritative path when it fires; this is purely a fallback.
            return true;
        }
    }

    private void FlushPending()
    {
        PendingTextChange? toFinalize;
        lock (_lock)
        {
            toFinalize = _pending;
            _pending = null;
        }

        if (toFinalize is not null)
        {
            FinalizeTextChange(toFinalize);
        }
    }

    private void FinalizeTextChange(PendingTextChange pending)
    {
        string currentValue;
        try
        {
            currentValue = pending.Element.Patterns.Value.IsSupported
                ? pending.Element.Patterns.Value.Pattern.Value ?? string.Empty
                : string.Empty;
        }
        catch (Exception)
        {
            currentValue = string.Empty;
        }

        TryCapture(pending.Element, ActionType.TypeText, currentValue);
    }

    private void PollToggleStates()
    {
        List<AutomationElement> changed = new();

        try
        {
            var checkBoxes = _inspector.GetActionableElements(_window).Where(e => e.ControlType == "CheckBox");

            lock (_lock)
            {
                foreach (var el in checkBoxes)
                {
                    var key = GetIdentityKey(el.Element);
                    if (key is null || !el.Element.Patterns.Toggle.IsSupported)
                    {
                        continue;
                    }

                    ToggleState current;
                    try
                    {
                        current = el.Element.Patterns.Toggle.Pattern.ToggleState.Value;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    // First observation just establishes the baseline — only a change from a
                    // previously-seen state counts as something the user did.
                    if (_lastToggleStates.TryGetValue(key, out var previous) && previous != current)
                    {
                        changed.Add(el.Element);
                    }

                    _lastToggleStates[key] = current;
                }
            }
        }
        catch (Exception)
        {
            // Best-effort polling — a transient failure just means we try again next tick.
        }

        foreach (var element in changed)
        {
            TryCapture(element, ActionType.Toggle, null);
        }
    }

    private void TryCapture(AutomationElement element, ActionType action, string? value)
    {
        try
        {
            var key = GetIdentityKey(element);
            if (key is null)
            {
                return;
            }

            // Re-resolve against a fresh snapshot rather than trusting the element from the
            // event: this keeps ordinal-fallback locators consistent with how the replay
            // engine (and the manual picker) compute them, and confirms the element is still
            // one of our supported, actionable control types.
            var freshElements = _inspector.GetActionableElements(_window);
            var match = freshElements.FirstOrDefault(e => GetIdentityKey(e.Element) == key);
            if (match is null)
            {
                return;
            }

            var locator = LocatorBuilder.Build(match, freshElements);
            _onActionCaptured(locator, action, value);
        }
        catch (Exception)
        {
            // Best-effort live capture — a single failed capture shouldn't stop recording.
        }
    }

    // Deliberately not RuntimeId: empirically, elements handed to us via UIA event callbacks
    // (as opposed to a fresh tree walk) can report RuntimeId as "not supported" even though
    // it's meant to be universally available — observed on WinForms TabItem headers. Every
    // control type in our supported set reliably exposes ControlType + (AutomationId or
    // Name), which is exactly what the fallback locator strategy already relies on elsewhere.
    private static string? GetIdentityKey(AutomationElement element)
    {
        try
        {
            var controlType = element.ControlType.ToString();

            string? automationId = null;
            try { automationId = element.AutomationId; } catch (Exception) { }
            if (!string.IsNullOrEmpty(automationId))
            {
                return $"{controlType}|id:{automationId}";
            }

            string? name = null;
            try { name = element.Name; } catch (Exception) { }
            return string.IsNullOrEmpty(name) ? null : $"{controlType}|name:{name}";
        }
        catch (Exception)
        {
            return null;
        }
    }
}
