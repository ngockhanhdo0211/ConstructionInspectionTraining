namespace ConstructionInspection.Application.Common.Exceptions;

public sealed class PayloadTooLargeException(
    string message,
    string code = "drawing.payload_too_large") : Exception(message)
{
    public string Code { get; } = code;
}
