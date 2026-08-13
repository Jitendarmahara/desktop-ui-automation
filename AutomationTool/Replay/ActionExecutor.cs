using AutomationTool.Exceptions;
using AutomationTool.Steps;
using FlaUI.Core.AutomationElements;

namespace AutomationTool.Replay;

/// <summary>Executes one action via UIA control patterns — never coordinates/pixels for identification.</summary>
public static class ActionExecutor
{
    public static void Execute(AutomationElement element, ActionType action, string? value)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));

        switch (action)
        {
            case ActionType.Click:
                ExecuteClick(element);
                break;
            case ActionType.TypeText:
                ExecuteTypeText(element, value);
                break;
            case ActionType.Toggle:
                ExecuteToggle(element);
                break;
            case ActionType.Select:
                ExecuteSelect(element);
                break;
            default:
                throw new AutomationToolException($"Unsupported action type: {action}");
        }
    }

    private static void ExecuteClick(AutomationElement element)
    {
        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return;
        }

        // Last-resort fallback for controls with no InvokePattern: the element was still
        // identified purely through the UIA tree, only the final input mechanism changes
        // to a simulated click at its reported bounding rectangle. Not image recognition.
        element.Click();
    }

    private static void ExecuteTypeText(AutomationElement element, string? value)
    {
        if (value is null)
        {
            throw new AutomationToolException("A TypeText action requires a value.");
        }

        if (!element.Patterns.Value.IsSupported)
        {
            throw new AutomationToolException($"Element does not support text entry (no ValuePattern): {element.ControlType}.");
        }

        element.Patterns.Value.Pattern.SetValue(value);
    }

    private static void ExecuteToggle(AutomationElement element)
    {
        if (!element.Patterns.Toggle.IsSupported)
        {
            throw new AutomationToolException($"Element does not support Toggle (no TogglePattern): {element.ControlType}.");
        }

        element.Patterns.Toggle.Pattern.Toggle();
    }

    private static void ExecuteSelect(AutomationElement element)
    {
        if (!element.Patterns.SelectionItem.IsSupported)
        {
            throw new AutomationToolException($"Element does not support Select (no SelectionItemPattern): {element.ControlType}.");
        }

        element.Patterns.SelectionItem.Pattern.Select();
    }
}
