using System.Text.RegularExpressions;

namespace ConstructionInspection.Domain.FormTemplates;

public static partial class FormTemplateRules
{
    public const int MaximumCodeLength = 50;
    public const int MaximumNameLength = 250;
    public const int MaximumDescriptionLength = 2000;

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public static bool IsValidCode(string code) => CodePattern().IsMatch(code);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{1,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
