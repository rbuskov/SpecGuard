using System.Text.Json;

namespace SpecGuard.Test.Schemas;

/// <summary>
/// Data-driven coverage of the USAGE.md §7.1 numeric ranges table. Each CLR
/// numeric type has a documented format, minimum, and maximum; these assertions
/// enforce that the built JSON Schema honors exactly those numbers so that the
/// published contract matches documentation.
/// </summary>
public class OpenApiSchemaBuilderUsageParityTests
{
    public static IEnumerable<object[]> IntegerRanges()
    {
        yield return new object[] { "int8",   -128L,                   127L };
        yield return new object[] { "uint8",  0L,                      255L };
        yield return new object[] { "int16",  -32768L,                 32767L };
        yield return new object[] { "uint16", 0L,                      65535L };
        yield return new object[] { "int32",  (long)int.MinValue,      (long)int.MaxValue };
        yield return new object[] { "uint32", 0L,                      (long)uint.MaxValue };
        yield return new object[] { "int64",  long.MinValue,           long.MaxValue };
    }

    [Theory]
    [MemberData(nameof(IntegerRanges))]
    public void Integer_format_emits_documented_long_range(string format, long min, long max)
    {
        var schema = Parse($$"""
            {
              "type": "object",
              "properties": { "v": { "type": "integer", "format": "{{format}}" } }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var prop = result["properties"]!["v"]!;

        Assert.Equal(min, prop["minimum"]!.GetValue<long>());
        Assert.Equal(max, prop["maximum"]!.GetValue<long>());
    }

    [Fact]
    public void Uint64_format_emits_documented_ulong_range()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": { "v": { "type": "integer", "format": "uint64" } }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var prop = result["properties"]!["v"]!;

        Assert.Equal((ulong)0, prop["minimum"]!.GetValue<ulong>());
        Assert.Equal(ulong.MaxValue, prop["maximum"]!.GetValue<ulong>());
    }

    public static IEnumerable<object[]> NumberRanges()
    {
        yield return new object[] { "float16", (double)-65504,           (double)65504 };
        yield return new object[] { "float",   (double)float.MinValue,   (double)float.MaxValue };
        yield return new object[] { "double",  double.MinValue,          double.MaxValue };
    }

    [Theory]
    [MemberData(nameof(NumberRanges))]
    public void Number_format_emits_documented_double_range(string format, double min, double max)
    {
        var schema = Parse($$"""
            {
              "type": "object",
              "properties": { "v": { "type": "number", "format": "{{format}}" } }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var prop = result["properties"]!["v"]!;

        Assert.Equal(min, prop["minimum"]!.GetValue<double>());
        Assert.Equal(max, prop["maximum"]!.GetValue<double>());
    }

    [Theory]
    [InlineData("example")]
    [InlineData("xml")]
    [InlineData("externalDocs")]
    [InlineData("deprecated")]
    [InlineData("writeOnly")]
    [InlineData("readOnly")]
    public void OpenApi_only_keywords_are_stripped_from_built_schema(string keyword)
    {
        var schema = Parse($$"""
            {
              "type": "object",
              "properties": {
                "v": { "type": "string", "{{keyword}}": "something" }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var prop = result["properties"]!["v"]!;

        Assert.Null(prop[keyword]);
    }

    [Theory]
    [InlineData("binary")]
    [InlineData("byte")]
    [InlineData("password")]
    public void Ignored_string_formats_are_stripped_from_built_schema(string format)
    {
        var schema = Parse($$"""
            {
              "type": "object",
              "properties": {
                "v": { "type": "string", "format": "{{format}}" }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var prop = result["properties"]!["v"]!;

        Assert.Null(prop["format"]);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
