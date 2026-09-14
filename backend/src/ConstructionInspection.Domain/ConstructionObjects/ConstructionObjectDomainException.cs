namespace ConstructionInspection.Domain.ConstructionObjects;

public sealed class ConstructionObjectDomainException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}
