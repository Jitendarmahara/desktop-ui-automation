using AutomationTool.Exceptions;
using AutomationTool.Steps;

namespace AutomationTool.TestModel;

public sealed class TestCase
{
    public string Name { get; init; } = string.Empty;
    public string TargetProcessName { get; init; } = string.Empty;
    public string TargetWindowTitle { get; init; } = string.Empty;
    public List<TestStep> Steps { get; init; } = new();

    public static TestCase Create(string name, string targetProcessName, string targetWindowTitle, IReadOnlyList<TestStep> steps)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AutomationToolException("Test name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(targetProcessName))
        {
            throw new AutomationToolException("Target process name cannot be empty.");
        }

        if (steps.Count == 0)
        {
            throw new AutomationToolException("A test must contain at least one step.");
        }

        return new TestCase
        {
            Name = name,
            TargetProcessName = targetProcessName,
            TargetWindowTitle = targetWindowTitle ?? string.Empty,
            Steps = steps.OrderBy(s => s.Order).ToList()
        };
    }
}
