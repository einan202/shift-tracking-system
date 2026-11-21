namespace ClockApp.Api.Auth;

/// <summary>Authorization policy names. Roles are read from the database and signed into the JWT; clients never choose them.</summary>
public static class Policies
{
    public const string Admin = "Admin";
    public const string Employee = "Employee";
}
