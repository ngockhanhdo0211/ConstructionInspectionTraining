namespace ConstructionInspection.Domain.Drawings;

public sealed class DrawingDocumentDomainException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}
