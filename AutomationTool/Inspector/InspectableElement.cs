using FlaUI.Core.AutomationElements;

namespace AutomationTool.Inspector;

/// <summary>
/// A live element plus its display-relevant properties, read once and cached so the
/// console list doesn't re-hit the UIA COM layer every time it's redrawn.
/// </summary>
public sealed class InspectableElement
{
    public AutomationElement Element { get; }
    public string ControlType { get; }
    public string Name { get; }
    public string? AutomationId { get; }

    public InspectableElement(AutomationElement element)
    {
        Element = element ?? throw new ArgumentNullException(nameof(element));
        ControlType = element.ControlType.ToString();
        Name = SafeRead(() => element.Name) ?? string.Empty;

        var automationId = SafeRead(() => element.AutomationId);
        AutomationId = string.IsNullOrEmpty(automationId) ? null : automationId;
    }

    // UIA is a COM layer over another process: property reads can throw for reasons that
    // have nothing to do with our code (element torn down mid-read, property genuinely
    // unsupported by that control). Treating an unreadable property as "absent" is the
    // correct behavior for a display list, not a bug to propagate.
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

    public override string ToString()
    {
        var idPart = AutomationId is not null ? $"AutomationId='{AutomationId}'" : "no AutomationId";
        var namePart = string.IsNullOrEmpty(Name) ? string.Empty : $" Name='{Name}'";
        return $"{ControlType}{namePart} ({idPart})";
    }
}
