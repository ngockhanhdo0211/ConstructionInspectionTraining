using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.FormTemplates.Interfaces;

namespace ConstructionInspection.Application.FormTemplates.Validation;

public sealed partial class FormIoSchemaValidator : IFormIoSchemaValidator
{
    public const int MaximumUtf8Bytes = 1_048_576;
    public const int MaximumComponents = 200;
    private static readonly HashSet<string> CommonProperties = new(StringComparer.Ordinal)
    {
        "type", "key", "label", "description", "placeholder", "tooltip", "input",
        "tableView", "hidden", "disabled", "defaultValue", "validate"
    };
    private static readonly Dictionary<string, HashSet<string>> TypeProperties = new(StringComparer.Ordinal)
    {
        ["textfield"] = Set("prefix", "suffix", "spellcheck"),
        ["textarea"] = Set("rows", "autoExpand", "spellcheck"),
        ["number"] = Set("decimalLimit", "requireDecimal", "prefix", "suffix"),
        ["select"] = Set("dataSrc", "data", "multiple"),
        ["radio"] = Set("values", "inline"),
        ["checkbox"] = Set(),
        ["datetime"] = Set("format", "enableDate", "enableTime")
    };
    private static readonly HashSet<string> DangerousProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "custom", "customConditional", "calculateValue", "calculateServer", "logic", "actions",
        "event", "events", "evalContext", "template", "content", "html", "url", "resource",
        "refreshOn", "redrawOn", "components"
    };
    private static readonly HashSet<string> TextValidation = Set("required", "minLength", "maxLength");
    private static readonly HashSet<string> NumberValidation = Set("required", "min", "max", "integer");
    private static readonly HashSet<string> RequiredValidation = Set("required");
    private static readonly HashSet<string> DateValidation = Set("required", "minDate", "maxDate");
    private static readonly HashSet<string> DateFormats = Set("yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm:ssXXX");

    public ValidatedFormIoSchema Validate(string schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson)) Fail("SchemaJson is required.");
        var byteCount = Encoding.UTF8.GetByteCount(schemaJson);
        if (byteCount > MaximumUtf8Bytes)
            throw new BadRequestException("form_template.schema_too_large", "SchemaJson exceeds 1,048,576 UTF-8 bytes.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(schemaJson, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
        }
        catch (JsonException) { Fail("SchemaJson is not valid JSON or exceeds maximum depth 16."); throw; }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) Fail("Schema root must be an object.");
            var rootProperties = root.EnumerateObject().ToArray();
            if (rootProperties.Length != 1 || rootProperties[0].Name != "components")
                Fail("Schema root may contain only the components property.");
            var components = rootProperties[0].Value;
            if (components.ValueKind != JsonValueKind.Array) Fail("components must be an array.");
            if (components.GetArrayLength() > MaximumComponents) Fail("A schema may contain at most 200 components.");

            ScanDangerousContent(root, isRoot: true);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var component in components.EnumerateArray()) ValidateComponent(component, keys);

            return new ValidatedFormIoSchema(JsonSerializer.Serialize(root), byteCount);
        }
    }

    private static void ValidateComponent(JsonElement component, HashSet<string> keys)
    {
        if (component.ValueKind != JsonValueKind.Object) Fail("Every component must be an object.");
        var type = RequiredString(component, "type", 50);
        if (!TypeProperties.TryGetValue(type, out var typeProperties)) Fail($"Component type '{type}' is not supported.");
        var key = RequiredString(component, "key", 100);
        if (!KeyPattern().IsMatch(key)) Fail("Component key format is invalid.");
        if (!keys.Add(key)) Fail($"Component key '{key}' is duplicated.");
        RequiredString(component, "label", 250);

        foreach (var property in component.EnumerateObject())
        {
            if (!CommonProperties.Contains(property.Name) && !typeProperties.Contains(property.Name))
                Fail($"Property '{property.Name}' is not allowed for component type '{type}'.");
        }

        OptionalString(component, "description", 1000);
        OptionalString(component, "placeholder", 250);
        OptionalString(component, "tooltip", 500);
        OptionalBoolean(component, "tableView");
        OptionalBoolean(component, "hidden");
        OptionalBoolean(component, "disabled");
        if (component.TryGetProperty("input", out var input) && input.ValueKind != JsonValueKind.True)
            Fail("input, when supplied, must be true.");

        switch (type)
        {
            case "textfield": ValidateText(component, false); break;
            case "textarea": ValidateText(component, true); break;
            case "number": ValidateNumber(component); break;
            case "select": ValidateSelect(component); break;
            case "radio": ValidateRadio(component); break;
            case "checkbox": ValidateCheckbox(component); break;
            case "datetime": ValidateDateTime(component); break;
        }
    }

    private static void ValidateText(JsonElement component, bool textArea)
    {
        OptionalString(component, "prefix", 100);
        OptionalString(component, "suffix", 100);
        OptionalBoolean(component, "spellcheck");
        OptionalBoolean(component, "autoExpand");
        if (component.TryGetProperty("rows", out var rows)
            && (!rows.TryGetInt32(out var value) || value is < 1 or > 20)) Fail("rows must be between 1 and 20.");
        if (!textArea && (component.TryGetProperty("rows", out _) || component.TryGetProperty("autoExpand", out _)))
            Fail("Text Field does not support textarea properties.");
        var validation = ValidateObject(component, TextValidation);
        var min = OptionalInt(validation, "minLength", 0, 1_000_000);
        var max = OptionalInt(validation, "maxLength", 0, 1_000_000);
        if (min.HasValue && max.HasValue && min > max) Fail("minLength cannot exceed maxLength.");
        ValidateDefault(component, JsonValueKind.String);
    }

    private static void ValidateNumber(JsonElement component)
    {
        OptionalString(component, "prefix", 100);
        OptionalString(component, "suffix", 100);
        OptionalBoolean(component, "requireDecimal");
        OptionalInt(component, "decimalLimit", 0, 10);
        var validation = ValidateObject(component, NumberValidation);
        var min = OptionalDouble(validation, "min");
        var max = OptionalDouble(validation, "max");
        OptionalBoolean(validation, "integer");
        if (min.HasValue && max.HasValue && min > max) Fail("min cannot exceed max.");
        ValidateDefault(component, JsonValueKind.Number);
    }

    private static void ValidateSelect(JsonElement component)
    {
        if (!component.TryGetProperty("dataSrc", out var dataSrc)
            || dataSrc.ValueKind != JsonValueKind.String || dataSrc.GetString() != "values")
            Fail("Select dataSrc must be 'values'.");
        if (!component.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            Fail("Select data.values is required.");
        if (data.EnumerateObject().Any(property => property.Name != "values"))
            Fail("Select data may contain only values.");
        if (!data.TryGetProperty("values", out var values))
            Fail("Select data.values is required.");
        ValidateOptions(values);
        OptionalBoolean(component, "multiple");
        ValidateObject(component, RequiredValidation);
        if (component.TryGetProperty("defaultValue", out var defaultValue))
        {
            var multiple = component.TryGetProperty("multiple", out var multipleElement) && multipleElement.ValueKind == JsonValueKind.True;
            if (multiple)
            {
                if (defaultValue.ValueKind != JsonValueKind.Array || defaultValue.EnumerateArray().Any(value => !IsPrimitive(value)))
                    Fail("Select multiple defaultValue must be an array of primitive values.");
            }
            else if (!IsPrimitive(defaultValue)) Fail("Select defaultValue must be a primitive value.");
        }
    }

    private static void ValidateRadio(JsonElement component)
    {
        if (!component.TryGetProperty("values", out var values)) Fail("Radio values is required.");
        ValidateOptions(values);
        OptionalBoolean(component, "inline");
        ValidateObject(component, RequiredValidation);
        ValidateDefault(component, JsonValueKind.String);
    }

    private static void ValidateCheckbox(JsonElement component)
    {
        ValidateObject(component, RequiredValidation);
        ValidateDefault(component, JsonValueKind.True, JsonValueKind.False);
    }

    private static void ValidateDateTime(JsonElement component)
    {
        var dateEnabled = !component.TryGetProperty("enableDate", out var date) || RequireBoolean(date, "enableDate");
        var timeEnabled = !component.TryGetProperty("enableTime", out var time) || RequireBoolean(time, "enableTime");
        if (!dateEnabled && !timeEnabled) Fail("Date/Time must enable date or time.");
        if (component.TryGetProperty("format", out var format)
            && (format.ValueKind != JsonValueKind.String || !DateFormats.Contains(format.GetString()!)))
            Fail("Date/Time format is not supported.");
        var validation = ValidateObject(component, DateValidation);
        var min = OptionalDate(validation, "minDate");
        var max = OptionalDate(validation, "maxDate");
        if (min.HasValue && max.HasValue && min > max) Fail("minDate cannot exceed maxDate.");
        ValidateDefault(component, JsonValueKind.String);
        if (component.TryGetProperty("defaultValue", out var defaultValue))
            ParseIsoDate(defaultValue, "defaultValue");
    }

    private static JsonElement? ValidateObject(JsonElement component, HashSet<string> allowed)
    {
        if (!component.TryGetProperty("validate", out var value)) return null;
        if (value.ValueKind != JsonValueKind.Object) Fail("validate must be an object.");
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name)) Fail($"Validation property '{property.Name}' is not supported.");
        OptionalBoolean(value, "required");
        return value;
    }

    private static void ValidateOptions(JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0) Fail("At least one option is required.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in values.EnumerateArray())
        {
            if (option.ValueKind != JsonValueKind.Object
                || option.EnumerateObject().Any(property => property.Name is not ("label" or "value"))) Fail("Option may contain only label and value.");
            RequiredString(option, "label", 250);
            if (!option.TryGetProperty("value", out var value) || !IsPrimitive(value)) Fail("Option value must be a primitive.");
            if (!seen.Add(value.GetRawText())) Fail("Option values must be unique.");
        }
    }

    private static void ScanDangerousContent(JsonElement element, bool isRoot = false)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (!(isRoot && property.Name == "components") && DangerousProperties.Contains(property.Name))
                    Fail($"Property '{property.Name}' is forbidden.");
                ScanDangerousContent(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) ScanDangerousContent(child);
        else if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString() ?? string.Empty;
            if (value.Contains("<script", StringComparison.OrdinalIgnoreCase)
                || value.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
                || value.Contains(";base64,", StringComparison.OrdinalIgnoreCase))
                Fail("Executable HTML, URLs and base64 payloads are forbidden.");
        }
    }

    private static string RequiredString(JsonElement element, string name, int maximum)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > maximum)
            Fail($"{name} is required and must not exceed {maximum} characters.");
        return value.GetString()!;
    }

    private static void OptionalString(JsonElement element, string name, int maximum)
    {
        if (element.TryGetProperty(name, out var value)
            && (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > maximum))
            Fail($"{name} must be a string no longer than {maximum} characters.");
    }

    private static void OptionalBoolean(JsonElement? element, string name)
    {
        if (element is { } actual) OptionalBoolean(actual, name);
    }

    private static void OptionalBoolean(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value)) RequireBoolean(value, name);
    }

    private static bool RequireBoolean(JsonElement value, string name)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Fail($"{name} must be boolean.");
        return value.GetBoolean();
    }

    private static int? OptionalInt(JsonElement? element, string name, int min, int max) =>
        element is { } actual ? OptionalInt(actual, name, min, max) : null;

    private static int? OptionalInt(JsonElement element, string name, int min, int max)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (!value.TryGetInt32(out var result) || result < min || result > max) Fail($"{name} is outside its allowed range.");
        return result;
    }

    private static double? OptionalDouble(JsonElement? element, string name)
    {
        if (element is not { } actual || !actual.TryGetProperty(name, out var value)) return null;
        if (!value.TryGetDouble(out var result) || !double.IsFinite(result)) Fail($"{name} must be a finite number.");
        return result;
    }

    private static DateTimeOffset? OptionalDate(JsonElement? element, string name)
    {
        if (element is not { } actual || !actual.TryGetProperty(name, out var value)) return null;
        return ParseIsoDate(value, name);
    }

    private static DateTimeOffset ParseIsoDate(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String)
            Fail($"{name} must be a literal ISO 8601 value.");
        if (!DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var result))
            Fail($"{name} must be a literal ISO 8601 value.");
        return result;
    }

    private static void ValidateDefault(JsonElement component, params JsonValueKind[] allowed)
    {
        if (component.TryGetProperty("defaultValue", out var value) && !allowed.Contains(value.ValueKind))
            Fail("defaultValue does not match the component output type.");
    }

    private static bool IsPrimitive(JsonElement value) => value.ValueKind is
        JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);

    [DoesNotReturn]
    private static void Fail(string message) => throw new BadRequestException("form_template.schema_invalid", message);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
