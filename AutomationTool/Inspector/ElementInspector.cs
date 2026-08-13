using System.Diagnostics;
using AutomationTool.Exceptions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace AutomationTool.Inspector;

/// <summary>
/// Attaches to a running target window and walks its UI Automation tree.
/// Owns the single UIA3Automation instance for the tool's lifetime.
/// </summary>
public sealed class ElementInspector : IDisposable
{
    // MVP control-type whitelist: textbox/button/label/checkbox, plus TabItem (tab headers)
    // so multi-tab forms can be navigated as part of a recorded test.
    private static readonly ControlType[] SupportedControlTypes =
    {
        ControlType.Button,
        ControlType.Edit,
        ControlType.Text,
        ControlType.CheckBox,
        ControlType.TabItem
    };

    // Standard Windows 10/11 title-bar caption buttons carry these fixed AutomationIds and
    // are exposed as real UIA Button elements belonging to the app's own window/process —
    // discovered empirically while testing, since they otherwise show up in every app's
    // element list regardless of what the app itself contains.
    private static readonly HashSet<string> SystemChromeAutomationIds =
        new(StringComparer.OrdinalIgnoreCase) { "Minimize-Restore", "Maximize-Restore", "Close", "System" };

    private readonly UIA3Automation _automation = new();

    /// <summary>Exposed so live event-driven recording (which needs EventLibrary/PropertyLibrary
    /// and focus-changed notifications) can share this instance rather than opening a second one.</summary>
    public UIA3Automation Automation => _automation;

    public Window Attach(string processName, string? titleContains, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            throw new AutomationToolException("Process name cannot be empty.");
        }

        var stopwatch = Stopwatch.StartNew();
        Exception? lastError = null;

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                // A stale instance left over from an earlier recording/run can still be alive
                // alongside the one the user actually means to target. Silently guessing which
                // one to attach to (e.g. "first found" or "most recently started") just turns
                // the ambiguity into a confusing failure several steps later, when some other
                // control isn't where this locator expects it to be. So: check every live
                // candidate, and only proceed when exactly one of them qualifies.
                var candidates = Process.GetProcessesByName(processName).Where(p => !p.HasExited).ToList();
                var matches = new List<(int Pid, Window Window)>();

                foreach (var candidate in candidates)
                {
                    try
                    {
                        var app = FlaUI.Core.Application.Attach(candidate.Id);
                        var window = app.GetMainWindow(_automation, TimeSpan.FromSeconds(1));
                        if (window is not null && TitleMatches(window, titleContains))
                        {
                            matches.Add((candidate.Id, window));
                        }
                    }
                    catch (Exception ex)
                    {
                        // This particular candidate isn't attachable right now (e.g. mid-exit) —
                        // other candidates may still resolve, so don't abort the whole attempt.
                        lastError = ex;
                    }
                }

                if (matches.Count == 1)
                {
                    var window = matches[0].Window;
                    // A window that has never been brought to the foreground can leave
                    // its client-area controls' UIA peers uncreated (observed: only the
                    // native title-bar chrome was visible to UIA until this was added).
                    // Focusing also matches how a real user would start automating.
                    TryFocus(window);
                    return window;
                }

                if (matches.Count > 1)
                {
                    var pids = string.Join(", ", matches.Select(m => m.Pid));
                    var titleHint = string.IsNullOrEmpty(titleContains)
                        ? " Narrow it down with a window-title filter, or close the extra instance(s)."
                        : " Close the extra instance(s), or use a more specific window-title filter.";
                    throw new AutomationToolException(
                        $"Found {matches.Count} running '{processName}' processes that match (PIDs: {pids}) — " +
                        $"refusing to guess which one to automate.{titleHint}");
                }
            }
            catch (AutomationToolException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            Thread.Sleep(250);
        }

        var titleSuffix = string.IsNullOrEmpty(titleContains) ? "." : $" with a title containing '{titleContains}'.";
        throw new AutomationToolException(
            $"Could not attach to a running process named '{processName}'{titleSuffix} " +
            "Make sure the target application is running.",
            lastError ?? new InvalidOperationException("No matching window was found."));
    }

    /// <summary>Elements suitable for the point-and-click builder's numbered list.</summary>
    /// <param name="includeOffscreen">
    /// False (default) excludes elements on an inactive tab/page — right for the manual picker,
    /// which should only offer things you could actually click right now. Pass true when
    /// re-resolving an element whose action already happened while it WAS visible (e.g.
    /// finalizing a text capture after the user has since switched tabs) — the edit is real and
    /// already done; it shouldn't be discarded just because a later, unrelated action moved the
    /// UI on before this one got a chance to finalize.
    /// </param>
    public IReadOnlyList<InspectableElement> GetActionableElements(Window window, bool includeOffscreen = false)
    {
        if (window is null) throw new ArgumentNullException(nameof(window));
        var ownerProcessId = GetOwnerProcessId(window);
        return WalkAll(window)
            .Where(e => IsActionable(e, ownerProcessId, includeOffscreen))
            .Select(e => new InspectableElement(e))
            .ToList();
    }

    /// <summary>
    /// Every actionable element matching a ControlType (and optionally Name), in the same
    /// traversal order used by <see cref="GetActionableElements"/>. Used by the fallback
    /// locator resolver so build-time ordinal capture and replay-time lookup agree.
    /// </summary>
    public IReadOnlyList<AutomationElement> FindMatches(Window window, ControlType controlType, string? name)
    {
        if (window is null) throw new ArgumentNullException(nameof(window));
        var ownerProcessId = GetOwnerProcessId(window);
        return WalkAll(window)
            .Where(e => IsActionable(e, ownerProcessId))
            .Where(e => e.ControlType == controlType)
            .Where(e => name is null || string.Equals(SafeRead(e), name, StringComparison.Ordinal))
            .ToList();
    }

    // Windows itself (via DWM) exposes title-bar caption buttons (Minimize/Maximize/Close)
    // as real UIA Button elements under the top-level window, owned by a different process
    // than the target app. Scoping to the window's own owner process excludes that OS chrome
    // noise without an arbitrary name/class blocklist.
    private static int? GetOwnerProcessId(Window window)
    {
        try
        {
            return window.Properties.ProcessId.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void TryFocus(Window window)
    {
        try
        {
            // A minimized (or otherwise unrealized) window never renders its client-area
            // controls, so UIA's native support bridge has nothing to expose for them —
            // observed directly: only the native title-bar chrome was visible until this
            // ran. WindowPattern.SetWindowVisualState forces it to a real, laid-out state.
            var windowPattern = window.Patterns.Window.PatternOrDefault;
            if (windowPattern is not null && windowPattern.WindowVisualState.Value == WindowVisualState.Minimized)
            {
                windowPattern.SetWindowVisualState(WindowVisualState.Normal);
            }

            window.Focus();
        }
        catch (Exception)
        {
            // Best-effort: a handful of legacy windows reject focus/restore requests; the
            // attach still succeeds, later steps just have a slightly higher chance of needing a retry.
        }
    }

    private static bool TitleMatches(Window window, string? titleContains)
    {
        if (string.IsNullOrEmpty(titleContains))
        {
            return true;
        }

        var title = SafeRead(() => window.Title);
        return title is not null && title.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsActionable(AutomationElement element, int? ownerProcessId, bool includeOffscreen = false)
    {
        if (!SupportedControlTypes.Contains(element.ControlType))
        {
            return false;
        }

        // Each property is read independently: some control types (e.g. TabItem headers
        // synthesized by the native Win32 tab control provider) genuinely don't support
        // AutomationId at all. Treating that as "element unusable" — rather than "this one
        // property is absent" — was silently excluding every tab from the picker.
        var automationId = SafeRead(() => element.AutomationId);
        if (!string.IsNullOrEmpty(automationId) && SystemChromeAutomationIds.Contains(automationId))
        {
            return false;
        }

        var processId = SafeReadInt(() => element.Properties.ProcessId.Value);
        if (ownerProcessId is not null && processId is not null && processId != ownerProcessId)
        {
            return false;
        }

        return includeOffscreen || SafeReadBool(() => element.IsOffscreen) == false;
    }

    private static string? SafeRead(AutomationElement element) => SafeRead(() => element.Name);

    private static string? SafeRead(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int? SafeReadInt(Func<int> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool? SafeReadBool(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<AutomationElement> WalkAll(AutomationElement root)
    {
        foreach (var child in TryGetChildren(root))
        {
            yield return child;
            foreach (var descendant in WalkAll(child))
            {
                yield return descendant;
            }
        }
    }

    private static AutomationElement[] TryGetChildren(AutomationElement element)
    {
        try
        {
            return element.FindAllChildren();
        }
        catch (Exception)
        {
            return Array.Empty<AutomationElement>();
        }
    }

    public void Dispose() => _automation.Dispose();
}
