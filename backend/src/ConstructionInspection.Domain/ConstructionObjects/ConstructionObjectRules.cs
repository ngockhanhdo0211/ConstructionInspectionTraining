using System.Text.RegularExpressions;

namespace ConstructionInspection.Domain.ConstructionObjects;

public static partial class ConstructionObjectRules
{
    public const int MinimumCodeLength = 2;
    public const int MaximumCodeLength = 100;
    public const int MaximumNameLength = 250;
    public const int MaximumLocationLength = 500;
    public const int MaximumDescriptionLength = 2000;
    public const int MaximumHierarchyDepth = 5;
    public const int HierarchyTraversalSafetyLimit = 32;

    public static string NormalizeCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.Trim().ToUpperInvariant();
    }

    public static bool IsValidCode(string normalizedCode) =>
        normalizedCode.Length is >= MinimumCodeLength and <= MaximumCodeLength
        && CodePattern().IsMatch(normalizedCode);

    public static bool IsParentTypeValid(
        ConstructionObjectType childType,
        ConstructionObjectType? parentType) => childType switch
        {
            ConstructionObjectType.Project => parentType is null,
            ConstructionObjectType.Building => parentType is ConstructionObjectType.Project,
            ConstructionObjectType.Floor => parentType is ConstructionObjectType.Building,
            ConstructionObjectType.Room => parentType is ConstructionObjectType.Floor,
            ConstructionObjectType.WorkItem => parentType is ConstructionObjectType.Room,
            _ => false
        };

    [GeneratedRegex("^[A-Z0-9][A-Z0-9._-]{1,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
