using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Domain.DrawingMappings;
using FluentValidation;

namespace ConstructionInspection.Application.DrawingMappings;

internal static class DrawingMappingRowVersion
{
    public static bool IsValid(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { return Convert.FromBase64String(value).Length > 0; }
        catch (FormatException) { return false; }
    }

    public static void EnsureMatches(DrawingObjectMapping entity, string supplied)
    {
        byte[] parsed;
        try { parsed = Convert.FromBase64String(supplied); }
        catch (FormatException) { throw new ValidationException("rowVersion must be valid Base64."); }

        if (!entity.RowVersion.AsSpan().SequenceEqual(parsed))
            throw new ConflictException("drawing_mapping.concurrency_conflict", "The Drawing mapping was changed by another request.");
    }
}
