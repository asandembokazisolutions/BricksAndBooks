namespace BricksAndBooks.Infrastructure;

/// <summary>
/// Builds the application's UFS-style login address from the verified registration identifier.
/// This creates an app login name only; it does not provision an external university mailbox.
/// </summary>
public static class UniversityAccountAddress
{
    public const string StudentDomain = "@ufs4life.ac.za";
    public const string StaffDomain = "@ufs.ac.za";

    public static bool IsValidStudentNumber(string? value) => IsDigitsOfLength(value, 10);

    public static bool IsValidEmployeeNumber(string? value) => IsDigitsOfLength(value, 6);

    public static string ForStudent(string studentNumber)
    {
        if (!IsValidStudentNumber(studentNumber))
            throw new ArgumentException("Student number must contain exactly 10 digits.", nameof(studentNumber));
        return studentNumber + StudentDomain;
    }

    public static string ForStaff(string employeeNumber)
    {
        if (!IsValidEmployeeNumber(employeeNumber))
            throw new ArgumentException("Employee number must contain exactly 6 digits.", nameof(employeeNumber));
        return employeeNumber + StaffDomain;
    }

    private static bool IsDigitsOfLength(string? value, int length)
    {
        if (value is null || value.Length != length) return false;
        foreach (var character in value)
        {
            if (character is < '0' or > '9') return false;
        }
        return true;
    }
}
