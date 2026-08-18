namespace Agent.Api.Data;

public sealed class MockEmployeeService
{
    private readonly IReadOnlyList<EmployeeRecord> _employees =
    [
        new(101, "Ahmed Ali", "Engineering", 14),
        new(102, "Sara Hassan", "Finance", 9),
        new(103, "Omar Khaled", "HR", 18)
    ];

    public EmployeeRecord? FindByName(string name)
    {
        var needle = name.Trim();
        if (needle.Length == 0)
        {
            return null;
        }

        return _employees.FirstOrDefault(employee =>
            employee.Name.Equals(needle, StringComparison.OrdinalIgnoreCase)
            || employee.Name.StartsWith(needle + " ", StringComparison.OrdinalIgnoreCase)
            || employee.Name.EndsWith(" " + needle, StringComparison.OrdinalIgnoreCase));
    }
}
