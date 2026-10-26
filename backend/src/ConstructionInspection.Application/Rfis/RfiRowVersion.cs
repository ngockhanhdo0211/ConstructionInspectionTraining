using ConstructionInspection.Application.Common.Exceptions;

namespace ConstructionInspection.Application.Rfis;

internal static class RfiRowVersion
{
    public static void Match(byte[] current, string supplied, string property)
    {
        byte[] value;
        try { value = Convert.FromBase64String(supplied); }
        catch (FormatException) { throw new BadRequestException("rfi.row_version_invalid", $"{property} must be valid Base64."); }
        if (!current.SequenceEqual(value))
            throw new ConflictException("rfi.concurrency_conflict", "The RFI was changed by another request.");
    }
}
