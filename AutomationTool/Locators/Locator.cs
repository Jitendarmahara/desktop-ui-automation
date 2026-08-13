namespace AutomationTool.Locators;

/// <summary>
/// A serializable, replay-time-resolvable description of one UI element.
/// Never holds a live element reference — only identifying properties.
/// </summary>
public sealed record Locator(
    LocatorStrategy Strategy,
    string? AutomationId,
    string ControlType,
    string? Name,
    int? Ordinal)
{
    public static Locator ForAutomationId(string automationId, string controlType) =>
        new(LocatorStrategy.AutomationId, automationId, controlType, Name: null, Ordinal: null);

    public static Locator ForControlTypeAndName(string controlType, string name, int ordinal) =>
        new(LocatorStrategy.ControlTypeNameOrdinal, AutomationId: null, controlType, name, ordinal);

    public static Locator ForControlTypeOnly(string controlType, int ordinal) =>
        new(LocatorStrategy.ControlTypeOrdinal, AutomationId: null, controlType, Name: null, ordinal);
}
