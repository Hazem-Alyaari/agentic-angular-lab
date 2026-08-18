namespace Agent.Api.Data;

public sealed record EmployeeRecord(
    int Id,
    string Name,
    string Department,
    int RemainingLeaveDays);
