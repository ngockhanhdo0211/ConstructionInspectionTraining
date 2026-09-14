namespace ConstructionInspection.Application.Common.Exceptions;

public sealed class ResourceNotFoundException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
