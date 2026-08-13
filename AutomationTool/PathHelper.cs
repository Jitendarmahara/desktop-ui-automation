using System.IO;

namespace AutomationTool;

internal static class PathHelper
{
    public static string ResolveSampleTestsDirectory()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "sample-tests");
        return Directory.Exists(candidate) ? Path.GetFullPath(candidate) : AppContext.BaseDirectory;
    }
}
