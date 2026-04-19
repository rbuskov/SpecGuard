using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpecGuard.Test.Schemas;

public class OpenApiSchemaBuilderNumericFormatTests
{
    [Fact]
    public void Converts_int8_format_to_range_constraints()
    {
        var prop = BuildAndGetProperty("integer", "int8");

        Assert.Null(prop["format"]);
        Assert.Equal(-128, prop["minimum"]!.GetValue<long>());
        Assert.Equal(127, prop["maximum"]!.GetValue<long>());
    }

    [Fact]
    public void Converts_uint8_format_to_range_constraints()
    {
        var prop = BuildAndGetProperty("integer", "uint8");

        Assert.Null(prop["format"]);
        Assert.Equal(0, prop["minimum"]!.GetValue<long>());
        Assert.Equal(255, prop["maximum"]!.GetValue<long>());
    }

    [Fact]
    public void Converts_int16_format_to_range_constraints()
    {
        var prop = BuildAndGetProperty("integer", "int16");

        Assert.Null(prop["format"]);
        Assert.Equal(-32768, prop["minimum"]!.GetValue<long>());
        Assert.Equal(32767, prop["maximum"]!.GetValue<long>());
    }

    [Fact]
    public void Converts_uint16_format_to_range_constraints()
    {
        var prop = BuildAndGetProperty("integer", "uint16");

        Assert.Null(prop["format"]);
        Assert.Equal(0, prop["minimum"]!.GetValue<long>());
        Assert.Equal(65535, prop["maximum"]!.GetValue<long>());
    }

    [Fact]
    public void Converts_int32_format_to_range_constraints()
    {
        var prop = BuildAndGetProperty("integer", "int32");

        Assert.Null(prop["format"]);
        Assert.Equal(-2147483648, prop["minimum"]!.GetValue<long>());
        Assert.Equal(2147483647, prop["maximum"]!.GetValue<long>());
    }

    [Fact]
    public void Converts_uint32_format_to_range_constraints()
    {
        var prop = BuildAndGetProperty("integer", "uint32");

        Assert.Null(prop["format"]);
        Assert.Equal(0, prop["minimum"]!.GetValue<long>());
        Assert.Equal(4294967295, prop["maximum"]!.GetValue<long>());
    }

    [Fact]
    public void Converts_int64_format_to_range_constraints()
    {
        var prop = BuildAndGetProperty("integer", "int64");

        Assert.Null(prop["format"]);
        Assert.Equal(long.MinValue, prop["minimum"]!.GetValue<long>());
        Assert.Equal(long.MaxValue, prop["maximum"]!.GetValue<long>());
    }

    [Fact]
    public void Converts_uint64_format_to_range_constraints()
    {
        var prop = BuildAndGetProperty("integer", "uint64");

        Assert.Null(prop["format"]);
        Assert.Equal((ulong)0, prop["minimum"]!.GetValue<ulong>());
        Assert.Equal(ulong.MaxValue, prop["maximum"]!.GetValue<ulong>());
    }

    [Fact]
    public void Converts_float16_format_to_range_constraints()
    {
        var prop = BuildAndGetProperty("number", "float16");

        Assert.Null(prop["format"]);
        Assert.Equal(-65504.0, prop["minimum"]!.GetValue<double>());
        Assert.Equal(65504.0, prop["maximum"]!.GetValue<double>());
    }

    [Fact]
    public void Converts_float_format_to_range_constraints()
    {
        var prop = BuildAndGetProperty("number", "float");

        Assert.Null(prop["format"]);
        Assert.Equal((double)float.MinValue, prop["minimum"]!.GetValue<double>());
        Assert.Equal((double)float.MaxValue, prop["maximum"]!.GetValue<double>());
    }

    [Fact]
    public void Converts_double_format_to_range_constraints()
    {
        var prop = BuildAndGetProperty("number", "double");

        Assert.Null(prop["format"]);
        Assert.Equal(double.MinValue, prop["minimum"]!.GetValue<double>());
        Assert.Equal(double.MaxValue, prop["maximum"]!.GetValue<double>());
    }

    [Fact]
    public void Preserves_explicit_minimum_over_format_range()
    {
        var schema = Parse($$"""
            {
              "type": "object",
              "properties": {
                "age": { "type": "integer", "format": "int32", "minimum": 0 }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var prop = result["properties"]!["age"]!;

        Assert.Null(prop["format"]);
        Assert.Equal(0, prop["minimum"]!.GetValue<long>());
        Assert.Equal(2147483647, prop["maximum"]!.GetValue<long>());
    }

    [Fact]
    public void Preserves_explicit_maximum_over_format_range()
    {
        var schema = Parse($$"""
            {
              "type": "object",
              "properties": {
                "age": { "type": "integer", "format": "int32", "maximum": 150 }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var prop = result["properties"]!["age"]!;

        Assert.Null(prop["format"]);
        Assert.Equal(-2147483648, prop["minimum"]!.GetValue<long>());
        Assert.Equal(150, prop["maximum"]!.GetValue<long>());
    }

    [Fact]
    public void Strips_non_numeric_openapi_formats_without_adding_constraints()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": {
                "file": { "type": "string", "format": "binary" },
                "encoded": { "type": "string", "format": "byte" },
                "secret": { "type": "string", "format": "password" }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var props = result["properties"]!.AsObject();

        foreach (var prop in props)
        {
            Assert.Null(prop.Value!["format"]);
            Assert.Null(prop.Value!["minimum"]);
            Assert.Null(prop.Value!["maximum"]);
        }
    }

    [Fact]
    public void Applies_range_constraints_inside_component_defs()
    {
        var operationSchema = Parse("""{ "$ref": "#/components/schemas/Counter" }""");
        var components = Parse("""
            {
              "Counter": {
                "type": "object",
                "properties": {
                  "value": { "type": "integer", "format": "int32" }
                }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(operationSchema, components);
        var valueProp = result["$defs"]!["Counter"]!["properties"]!["value"]!;

        Assert.Null(valueProp["format"]);
        Assert.Equal(-2147483648, valueProp["minimum"]!.GetValue<long>());
        Assert.Equal(2147483647, valueProp["maximum"]!.GetValue<long>());
    }

    private static JsonNode BuildAndGetProperty(string type, string format)
    {
        var schema = Parse($$"""
            {
              "type": "object",
              "properties": {
                "value": { "type": "{{type}}", "format": "{{format}}" }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        return result["properties"]!["value"]!;
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
