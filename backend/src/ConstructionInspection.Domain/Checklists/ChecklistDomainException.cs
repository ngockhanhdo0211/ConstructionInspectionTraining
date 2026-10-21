namespace ConstructionInspection.Domain.Checklists;

public sealed class ChecklistDomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
