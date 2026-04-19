using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SpecGuard.Test.Validators;

public class JsonBodyValidatorNumberConstraintTests
{
    [Fact]
    public async Task Minimum_accepts_value_above_bound()
    {
        var result = await Validate("""{ "type": "number", "minimum": 10 }""", "11");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Minimum_accepts_value_at_bound()
    {
        var result = await Validate("""{ "type": "number", "minimum": 10 }""", "10");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Minimum_rejects_value_below_bound()
    {
        var result = await Validate("""{ "type": "number", "minimum": 10 }""", "9");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Maximum_accepts_value_below_bound()
    {
        var result = await Validate("""{ "type": "number", "maximum": 10 }""", "9");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Maximum_accepts_value_at_bound()
    {
        var result = await Validate("""{ "type": "number", "maximum": 10 }""", "10");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Maximum_rejects_value_above_bound()
    {
        var result = await Validate("""{ "type": "number", "maximum": 10 }""", "11");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task ExclusiveMinimum_rejects_value_at_bound()
    {
        // In OpenAPI 3.1 / JSON Schema 2020-12, exclusiveMinimum is a number, not a boolean.
        var result = await Validate("""{ "type": "number", "exclusiveMinimum": 10 }""", "10");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task ExclusiveMinimum_accepts_value_above_bound()
    {
        var result = await Validate("""{ "type": "number", "exclusiveMinimum": 10 }""", "10.5");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExclusiveMinimum_rejects_value_below_bound()
    {
        var result = await Validate("""{ "type": "number", "exclusiveMinimum": 10 }""", "9");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task ExclusiveMaximum_rejects_value_at_bound()
    {
        var result = await Validate("""{ "type": "number", "exclusiveMaximum": 10 }""", "10");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task ExclusiveMaximum_accepts_value_below_bound()
    {
        var result = await Validate("""{ "type": "number", "exclusiveMaximum": 10 }""", "9.5");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExclusiveMaximum_rejects_value_above_bound()
    {
        var result = await Validate("""{ "type": "number", "exclusiveMaximum": 10 }""", "11");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task MultipleOf_accepts_integer_multiple()
    {
        var result = await Validate("""{ "type": "integer", "multipleOf": 5 }""", "25");

        Assert.Empty(result);
    }

    [Fact]
    public async Task MultipleOf_rejects_non_multiple()
    {
        var result = await Validate("""{ "type": "integer", "multipleOf": 5 }""", "23");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task MultipleOf_accepts_zero()
    {
        // Zero is a multiple of any non-zero divisor.
        var result = await Validate("""{ "type": "integer", "multipleOf": 5 }""", "0");

        Assert.Empty(result);
    }

    [Fact]
    public async Task MultipleOf_accepts_negative_multiple()
    {
        var result = await Validate("""{ "type": "integer", "multipleOf": 5 }""", "-15");

        Assert.Empty(result);
    }

    [Fact]
    public async Task MultipleOf_float_divisor_accepts_multiple()
    {
        var result = await Validate("""{ "type": "number", "multipleOf": 0.1 }""", "0.3");

        Assert.Empty(result);
    }

    [Fact]
    public async Task MultipleOf_float_divisor_rejects_non_multiple()
    {
        var result = await Validate("""{ "type": "number", "multipleOf": 0.1 }""", "0.15");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Combined_minimum_and_maximum_form_inclusive_range()
    {
        var schema = """{ "type": "integer", "minimum": 1, "maximum": 5 }""";

        Assert.Empty(await Validate(schema, "1"));
        Assert.Empty(await Validate(schema, "5"));
        Assert.NotEmpty(await Validate(schema, "0"));
        Assert.NotEmpty(await Validate(schema, "6"));
    }

    [Fact]
    public async Task Number_constraints_apply_to_integer_type()
    {
        // Numeric keywords work on both "integer" and "number" types.
        var schema = """{ "type": "integer", "minimum": 1, "maximum": 100, "multipleOf": 2 }""";

        Assert.Empty(await Validate(schema, "42"));
        Assert.NotEmpty(await Validate(schema, "41"));
        Assert.NotEmpty(await Validate(schema, "0"));
        Assert.NotEmpty(await Validate(schema, "102"));
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
