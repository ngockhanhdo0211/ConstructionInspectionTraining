namespace ConstructionInspection.Application.Common.Exceptions;

public sealed class PayloadTooLargeException(string message) : Exception(message);
