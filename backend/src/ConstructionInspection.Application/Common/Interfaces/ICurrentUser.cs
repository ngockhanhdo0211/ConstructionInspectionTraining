namespace ConstructionInspection.Application.Common.Interfaces;

public interface ICurrentUser
{
    Guid? UserId { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsInRole(string role);
}
