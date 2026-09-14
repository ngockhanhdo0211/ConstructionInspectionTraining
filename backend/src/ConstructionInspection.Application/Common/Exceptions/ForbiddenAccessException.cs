namespace ConstructionInspection.Application.Common.Exceptions;

public sealed class ForbiddenAccessException(string message = "You are not allowed to perform this action.")
    : Exception(message);
