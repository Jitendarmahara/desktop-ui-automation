namespace AutomationTool.Reporting;

public sealed record StepResult(int Order, string Description, bool Passed, string? FailureReason);
