using AutomationTool.Inspector;

namespace AutomationTool.Locators;

/// <summary>
/// Capture-time half of the locator strategy: turns a picked element into a serializable
/// <see cref="Locator"/>, preferring AutomationId and falling back to a positional match.
/// </summary>
public static class LocatorBuilder
{
    public static Locator Build(InspectableElement target, IReadOnlyList<InspectableElement> allElements)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (allElements is null) throw new ArgumentNullException(nameof(allElements));

        var automationId = target.AutomationId;
        if (!string.IsNullOrEmpty(automationId) && IsTrustworthy(automationId))
        {
            return Locator.ForAutomationId(automationId, target.ControlType);
        }

        if (!string.IsNullOrEmpty(target.Name))
        {
            var sameTypeAndName = allElements
                .Where(e => e.ControlType == target.ControlType && e.Name == target.Name)
                .ToList();
            return Locator.ForControlTypeAndName(target.ControlType, target.Name, IndexOf(sameTypeAndName, target));
        }

        var sameType = allElements.Where(e => e.ControlType == target.ControlType).ToList();
        return Locator.ForControlTypeOnly(target.ControlType, IndexOf(sameType, target));
    }

    // Discovered empirically: WinForms controls with no Control.Name still get a numeric
    // AutomationId from the native Win32-to-UIA bridge (observed to be handle-derived), and
    // it changes between app restarts. A developer-assigned AutomationId is never purely
    // numeric in practice, so treating an all-digit id as untrustworthy and preferring the
    // Name/ControlType fallback avoids baking an unstable value into a saved test.
    private static bool IsTrustworthy(string automationId) => !automationId.All(char.IsDigit);

    private static int IndexOf(IReadOnlyList<InspectableElement> list, InspectableElement target)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], target))
            {
                return i;
            }
        }

        throw new InvalidOperationException("Target element was not part of the provided element list.");
    }
}
