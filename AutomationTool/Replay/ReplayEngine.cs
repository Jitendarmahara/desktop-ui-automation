using AutomationTool.Exceptions;
using AutomationTool.Inspector;
using AutomationTool.Locators;
using AutomationTool.Reporting;
using AutomationTool.Steps;
using AutomationTool.TestModel;
using FlaUI.Core.AutomationElements;

namespace AutomationTool.Replay;

/// <summary>Loads nothing itself — takes an already-loaded <see cref="TestCase"/> and executes it.</summary>
public sealed class ReplayEngine
{
    // UIA control patterns apply instantly (no simulated keystrokes/mouse movement), so
    // without a pause the target app's UI changes faster than a person watching it can
    // follow. This exists purely so a human can visually confirm each step actually ran.
    private static readonly TimeSpan StepPause = TimeSpan.FromMilliseconds(700);

    private readonly ElementInspector _inspector;
    private readonly LocatorResolver _resolver;

    public ReplayEngine(ElementInspector inspector)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _resolver = new LocatorResolver(inspector);
    }

    public IReadOnlyList<StepResult> Run(TestCase testCase, Action<TestStep>? onStepStarting = null, Action<StepResult>? onStepCompleted = null)
    {
        if (testCase is null) throw new ArgumentNullException(nameof(testCase));

        var window = _inspector.Attach(testCase.TargetProcessName, testCase.TargetWindowTitle, TimeSpan.FromSeconds(10));

        var results = new List<StepResult>();
        foreach (var step in testCase.Steps.OrderBy(s => s.Order))
        {
            onStepStarting?.Invoke(step);

            var result = RunStep(window, step);
            results.Add(result);
            onStepCompleted?.Invoke(result);

            if (!result.Passed)
            {
                // Stop on first failure: one failed action almost always makes every later
                // step meaningless (e.g. can't verify a screen a failed Click never reached),
                // so partial-continuation would just produce noisy, misleading results.
                break;
            }

            Thread.Sleep(StepPause);
        }

        return results;
    }

    private StepResult RunStep(Window window, TestStep step)
    {
        try
        {
            var element = ElementWaiter.WaitForResolve(() => _resolver.TryResolve(window, step.Locator));

            return step.Kind switch
            {
                StepKind.Action => RunAction(step, element),
                StepKind.Assertion => RunAssertion(step, element),
                _ => new StepResult(step.Order, step.Description ?? string.Empty, false, $"Unknown step kind: {step.Kind}")
            };
        }
        catch (Exception ex)
        {
            return new StepResult(step.Order, step.Description ?? string.Empty, false, $"Unexpected error: {ex.Message}");
        }
    }

    private static StepResult RunAction(TestStep step, AutomationElement? element)
    {
        if (element is null)
        {
            return new StepResult(step.Order, step.Description ?? string.Empty, false, "Element was not found within the timeout.");
        }

        try
        {
            ActionExecutor.Execute(element, step.Action!.Value, step.Value);
            return new StepResult(step.Order, step.Description ?? string.Empty, true, null);
        }
        catch (AutomationToolException ex)
        {
            return new StepResult(step.Order, step.Description ?? string.Empty, false, ex.Message);
        }
    }

    private static StepResult RunAssertion(TestStep step, AutomationElement? element)
    {
        var outcome = AssertionEvaluator.Evaluate(element, step.Assertion!.Value, step.Value);
        return new StepResult(step.Order, step.Description ?? string.Empty, outcome.Passed, outcome.FailureReason);
    }
}
