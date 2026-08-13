using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutomationTool.Exceptions;

namespace AutomationTool.TestModel;

public static class TestSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Save(TestCase testCase, string filePath)
    {
        if (testCase is null) throw new ArgumentNullException(nameof(testCase));
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new AutomationToolException("File path cannot be empty.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            var json = JsonSerializer.Serialize(testCase, Options);
            File.WriteAllText(filePath, json);
        }
        catch (IOException ex)
        {
            throw new AutomationToolException($"Failed to write test file '{filePath}': {ex.Message}", ex);
        }
    }

    public static TestCase Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new AutomationToolException("File path cannot be empty.");
        }

        if (!File.Exists(filePath))
        {
            throw new AutomationToolException($"Test file not found: {filePath}");
        }

        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (IOException ex)
        {
            throw new AutomationToolException($"Failed to read test file '{filePath}': {ex.Message}", ex);
        }

        TestCase? testCase;
        try
        {
            testCase = JsonSerializer.Deserialize<TestCase>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new AutomationToolException($"Test file '{filePath}' is not valid JSON: {ex.Message}", ex);
        }

        if (testCase is null || testCase.Steps.Count == 0)
        {
            throw new AutomationToolException($"Test file '{filePath}' does not contain any steps.");
        }

        return testCase;
    }
}
