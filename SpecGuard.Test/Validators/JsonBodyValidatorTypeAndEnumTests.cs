using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SpecGuard.Test.Validators;

public class JsonBodyValidatorTypeAndEnumTests
{
    [Fact]
    public async Task Type_integer_accepts_whole_number()
    {
        var result = await Validate("""{ "type": "integer" }""", "42");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Type_integer_rejects_non_whole_number()
    {
        var result = await Validate("""{ "type": "integer" }""", "3.14");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Type_integer_rejects_string()
    {
        var result = await Validate("""{ "type": "integer" }""", "\"42\"");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Type_integer_accepts_whole_number_expressed_as_float()
    {
        // 5.0 is mathematically an integer; JSON Schema 2020-12 accepts it.
        var result = await Validate("""{ "type": "integer" }""", "5.0");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Type_number_accepts_integer()
    {
        var result = await Validate("""{ "type": "number" }""", "42");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Type_number_accepts_float()
    {
        var result = await Validate("""{ "type": "number" }""", "3.14");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Type_number_rejects_string()
    {
        var result = await Validate("""{ "type": "number" }""", "\"3.14\"");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Type_boolean_accepts_true_and_false()
    {
        Assert.Empty(await Validate("""{ "type": "boolean" }""", "true"));
        Assert.Empty(await Validate("""{ "type": "boolean" }""", "false"));
    }

    [Fact]
    public async Task Type_boolean_rejects_string_true()
    {
        var result = await Validate("""{ "type": "boolean" }""", "\"true\"");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Type_null_accepts_null()
    {
        var result = await Validate("""{ "type": "null" }""", "null");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Type_null_rejects_non_null()
    {
        var result = await Validate("""{ "type": "null" }""", "0");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Type_array_accepts_array()
    {
        var result = await Validate("""{ "type": "array" }""", "[1, 2, 3]");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Type_array_rejects_object()
    {
        var result = await Validate("""{ "type": "array" }""", "{}");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Type_object_accepts_object()
    {
        var result = await Validate("""{ "type": "object" }""", "{}");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Type_object_rejects_array()
    {
        var result = await Validate("""{ "type": "object" }""", "[]");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Type_array_form_accepts_any_listed_type()
    {
        var schema = """{ "type": ["string", "null"] }""";

        Assert.Empty(await Validate(schema, "\"hello\""));
        Assert.Empty(await Validate(schema, "null"));
    }

    [Fact]
    public async Task Type_array_form_rejects_unlisted_type()
    {
        var result = await Validate("""{ "type": ["string", "null"] }""", "42");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Enum_accepts_listed_value()
    {
        var result = await Validate("""{ "enum": ["red", "green", "blue"] }""", "\"green\"");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Enum_rejects_unlisted_value()
    {
        var result = await Validate("""{ "enum": ["red", "green", "blue"] }""", "\"yellow\"");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Enum_is_case_sensitive()
    {
        var result = await Validate("""{ "enum": ["red", "green", "blue"] }""", "\"RED\"");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Enum_supports_mixed_type_values()
    {
        var schema = """{ "enum": [1, "one", null, true] }""";

        Assert.Empty(await Validate(schema, "1"));
        Assert.Empty(await Validate(schema, "\"one\""));
        Assert.Empty(await Validate(schema, "null"));
        Assert.Empty(await Validate(schema, "true"));
        Assert.NotEmpty(await Validate(schema, "2"));
    }

    [Fact]
    public async Task Const_accepts_exact_match()
    {
        var result = await Validate("""{ "const": "fixed" }""", "\"fixed\"");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Const_rejects_different_value()
    {
        var result = await Validate("""{ "const": "fixed" }""", "\"other\"");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Const_rejects_different_type()
    {
        var result = await Validate("""{ "const": 42 }""", "\"42\"");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Const_uses_deep_equality_for_objects()
    {
        var schema = """{ "const": { "a": 1, "b": [2, 3] } }""";

        Assert.Empty(
            await Validate(schema, """{ "a": 1, "b": [2, 3] }"""));

        Assert.NotEmpty(await Validate(schema, """{ "a": 1, "b": [2, 4] }"""));
    }

    [Fact]
    public async Task Const_is_order_insensitive_for_object_properties()
    {
        // JSON object equality is key/value, not key-order.
        var result = await Validate(
            """{ "const": { "a": 1, "b": 2 } }""",
            """{ "b": 2, "a": 1 }""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Nullable_enum_invalid_value_produces_errors()
    {
        // anyOf with enum + null is the standard OpenAPI pattern for nullable enums.
        var schema = """{ "anyOf": [{ "type": "string", "enum": ["available", "pending", "sold"] }, { "type": "null" }] }""";

        var result = await Validate(schema, "\"invalid\"");

        Assert.NotEmpty(result);
    }

    private static async Task<IReadOnlyList<ValidationErrorResult.ValidationError>> Validate(string valueSchemaJson, string valueJson)
    {
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec(valueSchemaJson);
        validator.Initialize(spec);

        var body = $$"""{"value":{{valueJson}}}""";
        var context = BuildContext(body, "/items");

        return await validator.ValidateAsync(context, CancellationToken.None);
    }

    private static JsonDocument BuildSpec(string valueSchemaJson) => JsonDocument.Parse($$"""
        {
          "paths": {
            "/items": {
              "post": {
                "requestBody": {
                  "content": {
                    "application/json": {
                      "schema": {
                        "type": "object",
                        "properties": {
                          "value": {{valueSchemaJson}}
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """);

    private static DefaultHttpContext BuildContext(string body, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = new PathString(path);
        context.Items["SpecGuard.ParsedBody"] = JsonDocument.Parse(body).RootElement;
        return context;
    }
}
