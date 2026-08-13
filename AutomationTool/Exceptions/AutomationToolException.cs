namespace AutomationTool.Exceptions;

/// <summary>
/// A known, expected failure in the automation domain (element not found, target app
/// not attachable, test file invalid, etc.) — as opposed to an unexpected bug.
/// Callers can catch this specifically to show a clean message instead of a stack trace.
/// </summary>
public sealed class AutomationToolException : Exception
{
    public AutomationToolException(string message) : base(message)
    {
    }

    public AutomationToolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
