namespace AutomationTool.Locators;

/// <summary>How a <see cref="Locator"/> should be resolved back to a live element.</summary>
public enum LocatorStrategy
{
    /// <summary>Exact match on AutomationId. Preferred whenever the control exposes one.</summary>
    AutomationId,

    /// <summary>Fallback: Nth element matching ControlType + Name, in tree-traversal order.</summary>
    ControlTypeNameOrdinal,

    /// <summary>Last resort: Nth element matching ControlType alone, in tree-traversal order.</summary>
    ControlTypeOrdinal
}
