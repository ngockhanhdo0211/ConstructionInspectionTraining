namespace ConstructionInspection.Domain.Rfis;

public sealed class RfiDomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
