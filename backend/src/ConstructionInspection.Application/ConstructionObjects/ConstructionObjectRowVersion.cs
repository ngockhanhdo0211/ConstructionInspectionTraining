using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Domain.ConstructionObjects;

namespace ConstructionInspection.Application.ConstructionObjects;

internal static class ConstructionObjectRowVersion
{
    public static byte[] Parse(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            throw new FluentValidation.ValidationException("rowVersion must be valid Base64.");
        }
    }

    public static void EnsureMatches(ConstructionObject entity, string supplied)
    {
        if (!entity.RowVersion.AsSpan().SequenceEqual(Parse(supplied)))
        {
            throw new ConflictException(
                "construction_object.concurrency_conflict",
                "The Construction Object was changed by another request.");
        }
    }
}
