using AutomationTool.Exceptions;
using AutomationTool.Locators;

namespace AutomationTool.Steps;

/// <summary>
/// One recorded step. Flat by design (a single class with nullable Action/Assertion
/// fields, discriminated by Kind) rather than an Action/Assertion class hierarchy —
/// keeps JSON (de)serialization trivial for a two-kind MVP.
/// </summary>
public sealed class TestStep
{
    public int Order { get; init; }
    public StepKind Kind { get; init; }
    public Locator Locator { get; init; } = null!;
    public ActionType? Action { get; init; }
    public AssertionType? Assertion { get; init; }
    public string? Value { get; init; }
    public string? Description { get; init; }

    public static TestStep ForAction(int order, Locator locator, ActionType action, string? value, string? description = null)
    {
        if (locator is null) throw new ArgumentNullException(nameof(locator));
        if (action == ActionType.TypeText && value is null)
        {
            throw new AutomationToolException("A TypeText action requires a value to type.");
        }

        return new TestStep
        {
            Order = order,
            Kind = StepKind.Action,
            Locator = locator,
            Action = action,
            Value = value,
            Description = description ?? $"{action} on {locator.ControlType} ({DescribeLocator(locator)})"
        };
    }

    public static TestStep ForAssertion(int order, Locator locator, AssertionType assertion, string? expectedValue, string? description = null)
    {
        if (locator is null) throw new ArgumentNullException(nameof(locator));
        if (assertion == AssertionType.VerifyTextEquals && string.IsNullOrEmpty(expectedValue))
        {
            throw new AutomationToolException("A VerifyTextEquals assertion requires a non-empty expected value.");
        }

        return new TestStep
        {
            Order = order,
            Kind = StepKind.Assertion,
            Locator = locator,
            Assertion = assertion,
            Value = expectedValue,
            Description = description ?? $"{assertion} on {locator.ControlType} ({DescribeLocator(locator)})"
        };
    }

    private static string DescribeLocator(Locator locator) =>
        locator.Strategy == LocatorStrategy.AutomationId
            ? locator.AutomationId ?? string.Empty
            : $"{locator.Name}#{locator.Ordinal}";
}
