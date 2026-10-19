using System.Globalization;

namespace ConstructionInspection.Domain.Inspections;

public static class InspectionRules
{
    public const int MaximumNumberLength = 50;
    public const int MaximumTitleLength = 250;
    public const int MaximumDescriptionLength = 2000;
    public const int MaximumWorkflowNoteLength = 2000;
    public const int MaximumDataUtf8Bytes = 2 * 1024 * 1024;
    public const int MaximumJsonDepth = 16;

    public static string FormatNumber(DateTime occurredAtUtc, long sequence)
    {
        if (sequence <= 0)
            throw Error("inspection.invalid_sequence", "Inspection sequence must be positive.");

        var utc = EnsureUtc(occurredAtUtc);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"INS-{utc.Year:D4}-{sequence:D6}");
    }

    internal static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    internal static InspectionDomainException Error(string code, string message) => new(code, message);
}
