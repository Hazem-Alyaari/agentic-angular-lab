using System.Text.Json;

namespace Agent.Api.Tools;

internal static class ToolJson
{
    public static string Error(string message) =>
        JsonSerializer.Serialize(new { error = message });

    public static bool TryReadEmployeeName(
        string argumentsJson,
        out string employeeName,
        out string errorJson)
    {
        employeeName = string.Empty;
        errorJson = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);

            if (!document.RootElement.TryGetProperty("employeeName", out var nameElement)
                || nameElement.ValueKind != JsonValueKind.String)
            {
                errorJson = Error("Missing required argument employeeName.");
                return false;
            }

            employeeName = nameElement.GetString()?.Trim() ?? string.Empty;
            if (employeeName.Length == 0)
            {
                errorJson = Error("employeeName must be a non-empty string.");
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            errorJson = Error("Tool arguments were not valid JSON.");
            return false;
        }
    }

    public static JsonElement EmployeeNameParameters() =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                employeeName = new
                {
                    type = "string",
                    description = "The employee's first or full name, for example Ahmed or Ahmed Ali."
                }
            },
            required = new[] { "employeeName" },
            additionalProperties = false
        });
}
