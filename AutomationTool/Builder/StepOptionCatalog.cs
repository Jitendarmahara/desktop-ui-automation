using AutomationTool.Steps;

namespace AutomationTool.Builder;

/// <summary>One selectable action/assertion offered for a given control type.</summary>
public sealed record StepOption(string Label, StepKind Kind, ActionType? Action, AssertionType? Assertion, bool RequiresValue)
{
    public static StepOption ForAction(string label, ActionType action, bool requiresValue = false) =>
        new(label, StepKind.Action, action, null, requiresValue);

    public static StepOption ForAssertion(string label, AssertionType assertion, bool requiresValue = false) =>
        new(label, StepKind.Assertion, null, assertion, requiresValue);
}

/// <summary>Which actions/assertions make sense for a given ControlType — shared by any authoring UI.</summary>
public static class StepOptionCatalog
{
    public static IReadOnlyList<StepOption> GetOptionsFor(string controlType)
    {
        var options = new List<StepOption>();

        switch (controlType)
        {
            case "Button":
                options.Add(StepOption.ForAction("Click", ActionType.Click));
                break;
            case "Edit":
                options.Add(StepOption.ForAction("Type Text", ActionType.TypeText, requiresValue: true));
                options.Add(StepOption.ForAssertion("Verify Text Equals", AssertionType.VerifyTextEquals, requiresValue: true));
                break;
            case "Text":
                options.Add(StepOption.ForAssertion("Verify Text Equals", AssertionType.VerifyTextEquals, requiresValue: true));
                break;
            case "CheckBox":
                options.Add(StepOption.ForAction("Toggle (check/uncheck)", ActionType.Toggle));
                break;
            case "TabItem":
                options.Add(StepOption.ForAction("Select Tab", ActionType.Select));
                break;
        }

        options.Add(StepOption.ForAssertion("Verify Exists", AssertionType.VerifyExists));
        options.Add(StepOption.ForAssertion("Verify Enabled", AssertionType.VerifyEnabled));
        options.Add(StepOption.ForAssertion("Verify Visible", AssertionType.VerifyVisible));

        return options;
    }
}
