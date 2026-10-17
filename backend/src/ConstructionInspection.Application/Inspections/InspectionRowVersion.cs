using ConstructionInspection.Application.Common.Exceptions;

namespace ConstructionInspection.Application.Inspections;

internal static class InspectionRowVersion
{
    public static void EnsureMatches(byte[] current, string supplied, string propertyName)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(supplied); }
        catch (FormatException)
        {
            throw new BadRequestException("inspection.row_version_invalid", $"{propertyName} must be valid Base64.");
        }
        if (!current.SequenceEqual(expected))
            throw new ConflictException("inspection.concurrency_conflict", "The Inspection was changed by another request.");
    }
}
