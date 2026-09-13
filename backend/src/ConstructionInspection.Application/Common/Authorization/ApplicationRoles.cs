namespace ConstructionInspection.Application.Common.Authorization;

public static class ApplicationRoles
{
    public const string Inspector = "INSPECTOR";
    public const string Reviewer = "REVIEWER";
    public const string Manager = "MANAGER";
    public const string Administrator = "ADMINISTRATOR";

    public static readonly IReadOnlyCollection<string> All =
    [Inspector, Reviewer, Manager, Administrator];
}
