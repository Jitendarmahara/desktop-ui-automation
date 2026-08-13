using System.Diagnostics;
using FlaUI.Core.AutomationElements;

namespace AutomationTool.Replay;

/// <summary>
/// A single fixed-rate polling loop, used for every wait in the tool (build-time refresh
/// and replay). Deliberately not event-driven: UIA structure/property-changed events are
/// known to be unreliable across the COM/process boundary, so this sidesteps that failure
/// mode entirely rather than trying to compensate for missed events after the fact.
/// </summary>
public static class ElementWaiter
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public static AutomationElement? WaitForResolve(Func<AutomationElement?> resolve, TimeSpan? timeout = null)
    {
        if (resolve is null) throw new ArgumentNullException(nameof(resolve));
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            var found = resolve();
            if (found is not null)
            {
                return found;
            }

            if (stopwatch.Elapsed >= effectiveTimeout)
            {
                return null;
            }

            Thread.Sleep(PollInterval);
        }
    }
}
