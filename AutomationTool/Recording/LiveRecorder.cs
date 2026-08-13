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
    private readonly Action<string>? _onCaptureLost;
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

    // The live element's value is re-read at finalization time rather than trusting the event
    // args' payload directly — that isn't reliable when the change was made by a different UIA
    // client stack than the one listening (observed empirically — captured TypeText steps ended
    // up with empty values). SnapshotValue exists purely as a fallback for that live re-read:
    // captured while the element is confirmed alive (right as the change comes in), so there's
    // still something usable if the target app has since closed by finalization time.
    //
    // Locator is built eagerly too — right here, while the field is confirmed visible — rather
    // than re-resolved lazily at finalization time. That matters because finalization can end up
    // happening well after the user has switched to a different tab (e.g. type Email, then
    // immediately switch tabs: the tab-switch's own handler flushes this pending capture as its
    // first step, right after the switch already took effect), and WinForms genuinely removes an
    // inactive tab page's controls from the UIA tree at that point — not just marks them
    // offscreen, they're unwalkable — so any locator built later would find nothing to build.
    private sealed record PendingTextChange(string IdentityKey, AutomationElement Element, string SnapshotValue, Locator Locator);

    /// <param name="onCaptureLost">Called when a pending edit couldn't be turned into a step at
    /// all (e.g. the target app closed before it was finalized) — lets the caller surface that
    /// as a visible warning instead of the edit just silently not showing up.</param>
    public LiveRecorder(UIA3Automation automation, ElementInspector inspector, Window window,
        Action<Locator, ActionType, string?> onActionCaptured, Action<string>? onCaptureLost = null)
    {
        _onCaptureLost = onCaptureLost;
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

        string snapshot;
        try
        {
            snapshot = element.Patterns.Value.IsSupported ? element.Patterns.Value.Pattern.Value ?? string.Empty : string.Empty;
        }
        catch (Exception)
        {
            snapshot = string.Empty;
        }

        // Built now, while this element is confirmed alive (we're handling a live event from
        // it) — see the comment on PendingTextChange for why this can't wait until finalization.
        // includeOffscreen: true because a field can legitimately still be mid-edit while
        // temporarily not the topmost/onscreen one for a moment; it's still on its own active
        // tab right now, so it's findable — offscreen-ness isn't the thing being worked around
        // here, tab-removal is, and this element is still fully present in the tree at this
        // exact moment either way.
        Locator? locator;
        try
        {
            var freshElements = _inspector.GetActionableElements(_window, includeOffscreen: true);
            var match = freshElements.FirstOrDefault(e => GetIdentityKey(e.Element) == key);
            locator = match is not null ? LocatorBuilder.Build(match, freshElements) : null;
        }
        catch (Exception)
        {
            locator = null;
        }

        if (locator is null)
        {
            // Couldn't build a locator even though we're handling a live event from this exact
            // element — nothing safe to track. Extremely unlikely outside of a COM hiccup.
            return;
        }

        lock (_lock)
        {
            _pending = new PendingTextChange(key, element, snapshot, locator);
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
        PendingTextChange? candidate;
        lock (_lock)
        {
            candidate = _pending;
        }

        if (candidate is null)
        {
            return;
        }

        // Deliberately called outside the lock: HasKeyboardFocus is a live COM call, and for a
        // target whose process has died it can take a while to fail rather than failing fast —
        // holding _lock for that entire time would block Flush() (called from the UI thread on
        // Finish & Save) from ever finalizing this same pending capture in the meantime.
        if (StillHasFocus(candidate.Element))
        {
            return;
        }

        PendingTextChange? toFinalize = null;
        lock (_lock)
        {
            // Re-check identity: _pending may have moved on to a different field while this
            // was checking focus outside the lock.
            if (_pending is not null && _pending.IdentityKey == candidate.IdentityKey)
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
            // Live re-read failed — most likely the target app/window closed. Fall back to
            // the last known-good value captured while the element was still alive, rather
            // than silently losing whatever the user had actually typed.
            currentValue = pending.SnapshotValue;
        }

        // No re-resolution needed here: pending.Locator was already built while this field was
        // confirmed visible (see OnValueChanged). By the time this runs the user may well have
        // switched tabs since — re-resolving now would fail for that alone, nothing to do with
        // whether the target app is still alive. Using the pre-built locator sidesteps that
        // entirely: it describes the field itself (e.g. AutomationId="txtEmail"), which is valid
        // regardless of which tab happens to be active right now.
        try
        {
            _onActionCaptured(pending.Locator, ActionType.TypeText, currentValue);
        }
        catch (Exception)
        {
            // Only reachable if the caller's own capture handling throws — genuinely
            // unexpected at this point, but still worth surfacing rather than swallowing.
            _onCaptureLost?.Invoke(currentValue);
        }
    }

    private void PollToggleStates()
    {
        List<AutomationElement> changed = new();

        try
        {
            var checkBoxes = _inspector.GetActionableElements(_window).Where(e => e.ControlType == "CheckBox");

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
                    // Deliberately outside the lock: a live COM call, same reasoning as
                    // CheckPendingStillFocused — don't hold _lock for however long a dead
                    // target's provider takes to fail.
                    current = el.Element.Patterns.Toggle.Pattern.ToggleState.Value;
                }
                catch (Exception)
                {
                    continue;
                }

                lock (_lock)
                {
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

    private bool TryCapture(AutomationElement element, ActionType action, string? value)
    {
        try
        {
            var key = GetIdentityKey(element);
            if (key is null)
            {
                return false;
            }

            // Re-resolve against a fresh snapshot rather than trusting the element from the
            // event: this keeps ordinal-fallback locators consistent with how the replay
            // engine (and the manual picker) compute them, and confirms the element is still
            // one of our supported, actionable control types. Used for Click/Select/Toggle,
            // which capture immediately as their own event fires — no debounce delay, so no
            // risk of the tab having since switched away (unlike TypeText's finalize path,
            // which builds its locator eagerly instead — see PendingTextChange).
            var freshElements = _inspector.GetActionableElements(_window);
            var match = freshElements.FirstOrDefault(e => GetIdentityKey(e.Element) == key);
            if (match is null)
            {
                return false;
            }

            var locator = LocatorBuilder.Build(match, freshElements);
            _onActionCaptured(locator, action, value);
            return true;
        }
        catch (Exception)
        {
            // Best-effort live capture — a single failed capture shouldn't stop recording.
            return false;
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
