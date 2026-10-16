using ConstructionInspection.Application.Common.Exceptions;

namespace ConstructionInspection.Application.FormTemplates;

internal static class FormTemplateRowVersion
{
    public static byte[] Parse(string value, string propertyName)
    {
        try { return Convert.FromBase64String(value); }
        catch (FormatException)
        {
            throw new BadRequestException("form_template.row_version_invalid", $"{propertyName} must be valid Base64.");
        }
    }

    public static void EnsureMatches(byte[] current, string supplied, string propertyName)
    {
        if (!current.AsSpan().SequenceEqual(Parse(supplied, propertyName)))
            throw new ConflictException("form_template.concurrency_conflict", "The Form Template was changed by another request.");
    }
}
