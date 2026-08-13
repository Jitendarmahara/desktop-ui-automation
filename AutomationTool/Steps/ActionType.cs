namespace AutomationTool.Steps;

public enum ActionType
{
    Click,
    TypeText,
    Toggle,

    /// <summary>Selects an item — used for TabItem (switching tabs) via SelectionItemPattern.</summary>
    Select
}
