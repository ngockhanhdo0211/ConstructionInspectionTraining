using System.Text;

namespace ConstructionInspection.Domain.Rfis;

public static class RfiRules
{
    public const int MaximumNumberLength = 50;
    public const int MaximumSubjectLength = 200;
    public const int MaximumContentUtf8Bytes = 32 * 1024;
    public const int MaximumReasonLength = 2000;
    public static readonly TimeSpan ResponseSla = TimeSpan.FromHours(72);

    public static string RequiredText(string? value, int maximumLength, string code, string field)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength)
            throw Error(code, $"{field} is required and must not exceed {maximumLength} characters.");
        return normalized;
    }

    public static string RequiredUtf8(string? value, string code, string field)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || Encoding.UTF8.GetByteCount(normalized) > MaximumContentUtf8Bytes)
            throw Error(code, $"{field} is required and must not exceed {MaximumContentUtf8Bytes} UTF-8 bytes.");
        return normalized;
    }

    public static DateTime EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw Error("rfi.timestamp_invalid", "RFI timestamps must be UTC.");
        return value;
    }

    public static RfiDomainException Error(string code, string message) => new(code, message);
}
