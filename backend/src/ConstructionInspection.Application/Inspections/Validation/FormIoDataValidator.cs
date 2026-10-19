using System.Globalization;
using System.Text;
using System.Text.Json;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.FormTemplates.Interfaces;
using ConstructionInspection.Application.Inspections.Interfaces;
using ConstructionInspection.Domain.Inspections;

namespace ConstructionInspection.Application.Inspections.Validation;

public sealed class FormIoDataValidator(IFormIoSchemaValidator schemaValidator) : IFormIoDataValidator
{
    public ValidatedFormIoData ValidateDraft(string schemaJson, string dataJson)
        => Validate(schemaJson, dataJson, requireComplete: false);

    public ValidatedFormIoData ValidateSubmit(string schemaJson, string dataJson)
        => Validate(schemaJson, dataJson, requireComplete: true);

    private ValidatedFormIoData Validate(string schemaJson, string dataJson, bool requireComplete)
    {
        var schema = schemaValidator.Validate(schemaJson);
        if (Encoding.UTF8.GetByteCount(dataJson) > InspectionRules.MaximumDataUtf8Bytes)
            throw new PayloadTooLargeException(
                "Inspection DataJson exceeds 2 MiB UTF-8 bytes.",
                "inspection.form_data_too_large");

        using var schemaDocument = Parse(schema.CanonicalJson, "The pinned Form Template schema is invalid.");
        using var dataDocument = Parse(dataJson, "DataJson must be valid JSON with depth at most 16.");
        if (dataDocument.RootElement.ValueKind != JsonValueKind.Object)
            Fail("DataJson root must be an object.");

        var components = schemaDocument.RootElement.GetProperty("components")
            .EnumerateArray().ToDictionary(x => x.GetProperty("key").GetString()!, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in dataDocument.RootElement.EnumerateObject())
        {
            if (!seen.Add(property.Name)) Fail($"Response key '{property.Name}' is duplicated.");
            if (!components.TryGetValue(property.Name, out var component))
                Fail($"Response key '{property.Name}' is not defined by the pinned schema.");
            ValidateValue(component, property.Value);
        }

        if (requireComplete)
        {
            foreach (var component in components.Values)
            {
                if (!IsRequired(component)) continue;
                var key = component.GetProperty("key").GetString()!;
                if (!dataDocument.RootElement.TryGetProperty(key, out var value) || IsMissing(value, component))
                    Fail($"Response key '{key}' is required.");
            }
        }

        ScanDangerous(dataDocument.RootElement);
        var canonical = JsonSerializer.Serialize(dataDocument.RootElement);
        var bytes = Encoding.UTF8.GetByteCount(canonical);
        if (bytes > InspectionRules.MaximumDataUtf8Bytes)
            throw new PayloadTooLargeException(
                "Inspection DataJson exceeds 2 MiB UTF-8 bytes.",
                "inspection.form_data_too_large");
        return new(canonical, bytes);
    }

    private static bool IsRequired(JsonElement component) =>
        component.TryGetProperty("validate", out var validation)
        && validation.TryGetProperty("required", out var required)
        && required.ValueKind == JsonValueKind.True;

    private static bool IsMissing(JsonElement value, JsonElement component)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return true;
        if (value.ValueKind == JsonValueKind.String) return string.IsNullOrWhiteSpace(value.GetString());
        if (value.ValueKind == JsonValueKind.Array) return value.GetArrayLength() == 0;
        return component.GetProperty("type").GetString() == "checkbox"
            && value.ValueKind == JsonValueKind.False;
    }

    private static JsonDocument Parse(string json, string message)
    {
        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = InspectionRules.MaximumJsonDepth
            });
        }
        catch (JsonException) { throw new BadRequestException("inspection.form_data_invalid", message); }
    }

    private static void ValidateValue(JsonElement component, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return;
        var type = component.GetProperty("type").GetString();
        switch (type)
        {
            case "textfield":
            case "textarea":
                Require(value, JsonValueKind.String, type);
                break;
            case "number":
                ValidateNumber(component, value);
                break;
            case "select":
                ValidateSelect(component, value);
                break;
            case "radio":
                ValidateOption(value, component.GetProperty("values"), "radio");
                break;
            case "checkbox":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Fail("Checkbox value must be boolean.");
                break;
            case "datetime":
                ValidateDate(value);
                break;
            default:
                Fail($"Component type '{type}' is not supported.");
                break;
        }
    }

    private static void ValidateNumber(JsonElement component, JsonElement value)
    {
        Require(value, JsonValueKind.Number, "number");
        if (!value.TryGetDouble(out var number) || !double.IsFinite(number)) Fail("Number value must be finite.");
        if (!component.TryGetProperty("validate", out var validation)) return;
        if (validation.TryGetProperty("integer", out var integer) && integer.ValueKind == JsonValueKind.True && number != Math.Truncate(number))
            Fail("Number value must be an integer.");
        if (validation.TryGetProperty("min", out var min) && number < min.GetDouble()) Fail("Number value is below its minimum.");
        if (validation.TryGetProperty("max", out var max) && number > max.GetDouble()) Fail("Number value exceeds its maximum.");
    }

    private static void ValidateSelect(JsonElement component, JsonElement value)
    {
        var options = component.GetProperty("data").GetProperty("values");
        var multiple = component.TryGetProperty("multiple", out var setting) && setting.ValueKind == JsonValueKind.True;
        if (!multiple) { ValidateOption(value, options, "select"); return; }
        Require(value, JsonValueKind.Array, "select multiple");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            ValidateOption(item, options, "select multiple");
            if (!seen.Add(item.GetRawText())) Fail("Select multiple values must not contain duplicates.");
        }
    }

    private static void ValidateOption(JsonElement value, JsonElement options, string name)
    {
        if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) Fail($"{name} value must be primitive.");
        if (!options.EnumerateArray().Any(x => OptionEquals(x.GetProperty("value"), value)))
            Fail($"{name} value is not one of the configured options.");
    }

    private static bool OptionEquals(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind) return false;
        return expected.ValueKind switch
        {
            JsonValueKind.String => expected.GetString() == actual.GetString(),
            JsonValueKind.Number => expected.TryGetDecimal(out var left)
                && actual.TryGetDecimal(out var right) && left == right,
            JsonValueKind.True or JsonValueKind.False => expected.GetBoolean() == actual.GetBoolean(),
            _ => false
        };
    }

    private static void ValidateDate(JsonElement value)
    {
        Require(value, JsonValueKind.String, "datetime");
        if (!DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            Fail("Date/Time value must be ISO 8601.");
    }

    private static void ScanDangerous(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) ScanDangerous(property.Value);
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ScanDangerous(item);
        else if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString() ?? string.Empty;
            if (text.Contains("<script", StringComparison.OrdinalIgnoreCase)
                || text.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
                || text.Contains(";base64,", StringComparison.OrdinalIgnoreCase))
                Fail("Executable content and embedded files are forbidden.");
        }
    }

    private static void Require(JsonElement value, JsonValueKind kind, string name)
    {
        if (value.ValueKind != kind) Fail($"{name} value has an invalid type.");
    }

    private static void Fail(string message) =>
        throw new BadRequestException("inspection.form_data_invalid", message);
}
