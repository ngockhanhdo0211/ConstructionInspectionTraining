namespace ConstructionInspection.Domain.Inspections;

public sealed class InspectionDomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
