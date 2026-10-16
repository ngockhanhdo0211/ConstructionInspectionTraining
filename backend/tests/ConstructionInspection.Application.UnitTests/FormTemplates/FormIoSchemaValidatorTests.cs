using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.FormTemplates.Validation;

namespace ConstructionInspection.Application.UnitTests.FormTemplates;

public sealed class FormIoSchemaValidatorTests
{
    private readonly FormIoSchemaValidator _sut = new();

    [Fact]
    public void Validate_AcceptsAllSevenSupportedComponents()
    {
        const string schema = """
        {"components":[
          {"type":"textfield","key":"title","label":"Title","validate":{"required":true,"minLength":1,"maxLength":100}},
          {"type":"textarea","key":"notes","label":"Notes","rows":4,"autoExpand":false},
          {"type":"number","key":"quantity","label":"Quantity","validate":{"min":0,"max":100,"integer":true}},
          {"type":"select","key":"trade","label":"Trade","dataSrc":"values","data":{"values":[{"label":"Civil","value":"civil"}]}},
          {"type":"radio","key":"result","label":"Result","values":[{"label":"Pass","value":"pass"}]},
          {"type":"checkbox","key":"confirmed","label":"Confirmed","defaultValue":false},
          {"type":"datetime","key":"checkedAt","label":"Checked at","format":"yyyy-MM-dd","enableDate":true,"enableTime":false}
        ]}
        """;

        var result = _sut.Validate(schema);

        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(schema), result.InputUtf8ByteCount);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(
            System.Text.Json.JsonDocument.Parse(schema).RootElement), result.CanonicalJson);
    }

    [Theory]
    [InlineData("{\"components\":[{\"type\":\"file\",\"key\":\"f\",\"label\":\"File\"}]}")]
    [InlineData("{\"components\":[{\"type\":\"textfield\",\"key\":\"a\",\"label\":\"A\",\"custom\":\"return true\"}]}")]
    [InlineData("{\"components\":[{\"type\":\"textfield\",\"key\":\"a\",\"label\":\"A\",\"unknown\":true}]}")]
    [InlineData("{\"components\":[{\"type\":\"textfield\",\"key\":\"a\",\"label\":\"A\",\"components\":[]}]}")]
    public void Validate_RejectsUnsupportedOrExecutableSchema(string schema)
    {
        var error = Assert.Throws<BadRequestException>(() => _sut.Validate(schema));

        Assert.Equal("form_template.schema_invalid", error.Code);
    }

    [Fact]
    public void Validate_RejectsKeysDuplicatedCaseInsensitively()
    {
        const string schema = """
        {"components":[
          {"type":"textfield","key":"answer","label":"One"},
          {"type":"checkbox","key":"ANSWER","label":"Two"}
        ]}
        """;

        var error = Assert.Throws<BadRequestException>(() => _sut.Validate(schema));

        Assert.Equal("form_template.schema_invalid", error.Code);
    }

    [Fact]
    public void Validate_RejectsMoreThanTwoHundredComponents()
    {
        var components = Enumerable.Range(1, 201)
            .Select(index => $"{{\"type\":\"checkbox\",\"key\":\"key{index}\",\"label\":\"Item {index}\"}}");
        var schema = $"{{\"components\":[{string.Join(',', components)}]}}";

        var error = Assert.Throws<BadRequestException>(() => _sut.Validate(schema));

        Assert.Equal("form_template.schema_invalid", error.Code);
    }

    [Fact]
    public void Validate_UsesUtf8ByteLimit()
    {
        var schema = "{\"components\":[{\"type\":\"textfield\",\"key\":\"a\",\"label\":\""
            + new string('đ', FormIoSchemaValidator.MaximumUtf8Bytes / 2)
            + "\"}]}";

        var error = Assert.Throws<BadRequestException>(() => _sut.Validate(schema));

        Assert.Equal("form_template.schema_too_large", error.Code);
    }

    [Fact]
    public void Validate_AcceptsExactByteLimitAndCanonicalizesWhitespace()
    {
        const string compact = "{\"components\":[]}";
        var schema = compact + new string(' ', FormIoSchemaValidator.MaximumUtf8Bytes - compact.Length);

        var result = _sut.Validate(schema);

        Assert.Equal(FormIoSchemaValidator.MaximumUtf8Bytes, result.InputUtf8ByteCount);
        Assert.Equal(compact, result.CanonicalJson);
    }
}
