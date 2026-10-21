using ConstructionInspection.Application.Common.Exceptions;

namespace ConstructionInspection.Application.Checklists;

internal static class ChecklistRowVersion
{
    public static void EnsureMatches(byte[] current, string supplied, string propertyName)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(supplied); }
        catch (FormatException)
        {
            throw new BadRequestException("checklist.row_version_invalid", $"{propertyName} must be valid Base64.");
        }
        if (!current.SequenceEqual(expected))
            throw new ConflictException("checklist.concurrency_conflict", "The Checklist was changed by another request.");
    }
}
