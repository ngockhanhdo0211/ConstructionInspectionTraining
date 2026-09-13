namespace ConstructionInspection.Application.Common.Interfaces;

public interface ICurrentUser
{
    Guid? UserId { get; }
}
