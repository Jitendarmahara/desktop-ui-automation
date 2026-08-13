using AutomationTool.Exceptions;
using AutomationTool.Steps;
using FlaUI.Core.AutomationElements;

namespace AutomationTool.Replay;

public sealed record AssertionOutcome(bool Passed, string? FailureReason);

public static class AssertionEvaluator
{
    public static AssertionOutcome Evaluate(AutomationElement? element, AssertionType assertion, string? expectedValue)
    {
        return assertion switch
        {
            AssertionType.VerifyExists => EvaluateExists(element),
            AssertionType.VerifyTextEquals => EvaluateTextEquals(element, expectedValue),
            AssertionType.VerifyEnabled => EvaluateEnabled(element),
            AssertionType.VerifyVisible => EvaluateVisible(element),
            _ => throw new AutomationToolException($"Unsupported assertion type: {assertion}")
        };
    }

    private static AssertionOutcome EvaluateExists(AutomationElement? element) =>
        element is not null
            ? new AssertionOutcome(true, null)
            : new AssertionOutcome(false, "Element was not found.");

    private static AssertionOutcome EvaluateTextEquals(AutomationElement? element, string? expectedValue)
    {
        if (element is null)
        {
            return new AssertionOutcome(false, "Element was not found.");
        }

        // "Text equals" and "Value equals" collapse to the same check: for the control types
        // in scope, UIA doesn't expose a separate Value distinct from Text (a textbox's Value
        // *is* its text; a label has no Value pattern at all, only Name).
        var actual = ReadText(element);
        return string.Equals(actual, expectedValue, StringComparison.Ordinal)
            ? new AssertionOutcome(true, null)
            : new AssertionOutcome(false, $"Expected text '{expectedValue}' but found '{actual}'.");
    }

    private static AssertionOutcome EvaluateEnabled(AutomationElement? element)
    {
        if (element is null)
        {
            return new AssertionOutcome(false, "Element was not found.");
        }

        return element.IsEnabled
            ? new AssertionOutcome(true, null)
            : new AssertionOutcome(false, "Element is disabled.");
    }

    private static AssertionOutcome EvaluateVisible(AutomationElement? element)
    {
        if (element is null)
        {
            return new AssertionOutcome(false, "Element was not found.");
        }

        return !element.IsOffscreen
            ? new AssertionOutcome(true, null)
            : new AssertionOutcome(false, "Element is offscreen/not visible.");
    }

    private static string? ReadText(AutomationElement element) =>
        element.Patterns.Value.IsSupported ? element.Patterns.Value.Pattern.Value : element.Name;
}
