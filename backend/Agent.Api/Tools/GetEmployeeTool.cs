using System.Text.Json;
using Agent.Api.Data;

namespace Agent.Api.Tools;

public sealed class GetEmployeeTool(MockEmployeeService employees) : IAgentTool
{
    public string Name => "get_employee";

    public string Description =>
        "Look up an employee by name and return id, full name, and department.";

    public JsonElement Parameters { get; } = ToolJson.EmployeeNameParameters();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ToolJson.TryReadEmployeeName(argumentsJson, out var employeeName, out var errorJson))
        {
            return Task.FromResult(errorJson);
        }

        var employee = employees.FindByName(employeeName);
        if (employee is null)
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error = "Employee not found",
                employeeName
            }));
        }

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            id = employee.Id,
            name = employee.Name,
            department = employee.Department
        }));
    }
}
