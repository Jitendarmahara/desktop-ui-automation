using System.Windows;

namespace AutomationTool;

/// <summary>
/// Windows' focus-stealing prevention can leave a window created on a background thread
/// (like our recorder, which runs on its own STA thread) sitting silently behind every
/// other open window — fully visible and functional per Win32, but invisible to the user,
/// which looks exactly like the app hanging or crashing. Forcing Topmost briefly on load
/// is the standard workaround to make sure it actually reaches the foreground.
/// </summary>
internal static class WindowForegroundHelper
{
    public static void BringToFront(Window window)
    {
        window.Loaded += (_, _) =>
        {
            window.Topmost = true;
            window.Activate();
            window.Topmost = false;
        };
    }
}
