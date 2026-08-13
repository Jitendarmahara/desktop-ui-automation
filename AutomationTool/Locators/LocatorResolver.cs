using AutomationTool.Exceptions;
using AutomationTool.Inspector;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace AutomationTool.Locators;

/// <summary>
/// Replay-time half of the locator strategy: turns a <see cref="Locator"/> back into a
/// live element. Never caches results — every call re-queries the current UIA tree.
/// </summary>
public sealed class LocatorResolver
{
    private readonly ElementInspector _inspector;

    public LocatorResolver(ElementInspector inspector)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    /// <summary>Returns null if no matching element currently exists; callers decide whether to wait/retry.</summary>
    public AutomationElement? TryResolve(Window window, Locator locator)
    {
        if (window is null) throw new ArgumentNullException(nameof(window));
        if (locator is null) throw new ArgumentNullException(nameof(locator));

        try
        {
            return locator.Strategy switch
            {
                LocatorStrategy.AutomationId => ResolveByAutomationId(window, locator),
                LocatorStrategy.ControlTypeNameOrdinal => ResolveByOrdinal(window, locator, useName: true),
                LocatorStrategy.ControlTypeOrdinal => ResolveByOrdinal(window, locator, useName: false),
                _ => throw new AutomationToolException($"Unknown locator strategy: {locator.Strategy}")
            };
        }
        catch (AutomationToolException)
        {
            throw;
        }
        catch (Exception)
        {
            // A transient UIA/COM failure while resolving reads as "not found yet" so that
            // ElementWaiter's polling loop can retry instead of the whole run crashing.
            return null;
        }
    }

    private static AutomationElement? ResolveByAutomationId(Window window, Locator locator)
    {
        if (string.IsNullOrEmpty(locator.AutomationId))
        {
            throw new AutomationToolException("An AutomationId locator is missing its AutomationId value.");
        }

        return window.FindFirstDescendant(cf => cf.ByAutomationId(locator.AutomationId));
    }

    private AutomationElement? ResolveByOrdinal(Window window, Locator locator, bool useName)
    {
        if (locator.Ordinal is null || !Enum.TryParse<ControlType>(locator.ControlType, out var controlType))
        {
            throw new AutomationToolException($"Ordinal locator is missing required fields (ControlType/Ordinal): {locator}");
        }

        var matches = _inspector.FindMatches(window, controlType, useName ? locator.Name : null);
        return locator.Ordinal.Value < matches.Count ? matches[locator.Ordinal.Value] : null;
    }
}
