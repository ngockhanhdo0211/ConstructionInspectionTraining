using System.Globalization;

namespace ConstructionInspection.Domain.Checklists;

public static class ChecklistRules
{
    public const int MaximumTitleLength = 250;
    public const int MaximumDescriptionLength = 2000;
    public const int MaximumCriterionLength = 1000;
    public const int MaximumNoteLength = 2000;
    public const int MaximumItems = 200;
    public const int MaximumNumberLength = 50;

    public static string FormatNumber(DateTime utcNow, long sequence)
    {
        if (utcNow.Kind != DateTimeKind.Utc || sequence <= 0)
            throw Error("checklist.invalid_number", "A UTC timestamp and positive sequence are required.");
        return $"CHK-{utcNow.Year.ToString(CultureInfo.InvariantCulture)}-{sequence.ToString("D6", CultureInfo.InvariantCulture)}";
    }

    public static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : throw Error("checklist.utc_required", "UTC timestamps are required.");

    public static ChecklistDomainException Error(string code, string message) => new(code, message);
}
