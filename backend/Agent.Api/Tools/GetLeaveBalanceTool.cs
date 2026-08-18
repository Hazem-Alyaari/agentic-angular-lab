using System.Text.Json;
using Agent.Api.Data;

namespace Agent.Api.Tools;

public sealed class GetLeaveBalanceTool(MockEmployeeService employees) : IAgentTool
{
    public string Name => "get_leave_balance";

    public string Description =>
        "Look up remaining annual leave days for an employee by name.";

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
            employeeId = employee.Id,
            employeeName = employee.Name,
            remainingDays = employee.RemainingLeaveDays
        }));
    }
}
