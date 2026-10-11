namespace ConstructionInspection.Domain.DrawingMappings;

public sealed class DrawingObjectMappingDomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
