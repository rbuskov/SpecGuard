using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SpecGuard.Test.Validators;

public class JsonBodyValidatorStringNumericCoercionTests
{
    [Fact]
    public async Task Option_off_rejects_string_encoded_integer()
    {
        var schema = """{ "type": "integer" }""";

        var errors = await Validate(schema, "\"42\"", allowStringNumerics: false);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task Option_on_accepts_string_encoded_integer()
    {
        var schema = """{ "type": ["string", "integer"], "pattern": "^-?(?:0|[1-9]\\d*)$" }""";

        var errors = await Validate(schema, "\"42\"", allowStringNumerics: true);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Option_on_still_accepts_raw_numeric_integer()
    {
        var schema = """{ "type": ["string", "integer"], "pattern": "^-?(?:0|[1-9]\\d*)$" }""";

        var errors = await Validate(schema, "42", allowStringNumerics: true);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Option_on_enforces_maximum_on_string_value()
    {
        var schema = """
            {
              "type": ["string", "integer"],
              "pattern": "^-?(?:0|[1-9]\\d*)$",
              "maximum": 100
            }
            """;

        var errors = await Validate(schema, "\"101\"", allowStringNumerics: true);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task Option_on_enforces_minimum_on_string_value()
    {
        var schema = """
            {
              "type": ["string", "integer"],
              "pattern": "^-?(?:0|[1-9]\\d*)$",
              "minimum": 10
            }
            """;

        var errors = await Validate(schema, "\"5\"", allowStringNumerics: true);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task Option_on_enforces_format_range_on_string_value()
    {
        // int32 max is 2147483647 — value below fits, value above should fail after coercion.
        var schema = """
            {
              "type": ["string", "integer"],
              "format": "int32",
              "pattern": "^-?(?:0|[1-9]\\d*)$"
            }
            """;

        Assert.Empty(await Validate(schema, "\"2147483647\"", allowStringNumerics: true));
        Assert.NotEmpty(await Validate(schema, "\"2147483648\"", allowStringNumerics: true));
    }

    [Fact]
    public async Task Option_on_rejects_non_numeric_string_via_pattern()
    {
        var schema = """
            {
              "type": ["string", "integer"],
              "pattern": "^-?(?:0|[1-9]\\d*)$"
            }
            """;

        var errors = await Validate(schema, "\"banana\"", allowStringNumerics: true);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task Option_on_accepts_string_encoded_number_for_float_schema()
    {
        var schema = """
            {
              "type": ["string", "number"],
              "pattern": "^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$"
            }
            """;

        Assert.Empty(await Validate(schema, "\"3.14\"", allowStringNumerics: true));
    }

    [Fact]
    public async Task Option_on_enforces_maximum_after_coercing_number()
    {
        var schema = """
            {
              "type": ["string", "number"],
              "pattern": "^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$",
              "maximum": 5
            }
            """;

        Assert.NotEmpty(await Validate(schema, "\"6.5\"", allowStringNumerics: true));
    }

    [Fact]
    public async Task Option_on_coerces_values_inside_arrays()
    {
        var schema = """
            {
              "type": "array",
              "items": {
                "type": ["string", "integer"],
                "pattern": "^-?(?:0|[1-9]\\d*)$",
                "maximum": 10
              }
            }
            """;

        Assert.Empty(await Validate(schema, "[\"1\", \"10\"]", allowStringNumerics: true));
        Assert.NotEmpty(await Validate(schema, "[\"1\", \"11\"]", allowStringNumerics: true));
    }

    [Fact]
    public async Task Option_on_coerces_through_nullable_oneOf()
    {
        var schema = """
            {
              "oneOf": [
                { "type": "null" },
                {
                  "type": ["string", "integer"],
                  "pattern": "^-?(?:0|[1-9]\\d*)$",
                  "maximum": 10
                }
              ]
            }
            """;

        Assert.Empty(await Validate(schema, "null", allowStringNumerics: true));
        Assert.Empty(await Validate(schema, "\"5\"", allowStringNumerics: true));
        Assert.NotEmpty(await Validate(schema, "\"11\"", allowStringNumerics: true));
    }

    [Fact]
    public async Task Option_off_leaves_numeric_constraints_working_for_raw_numbers()
    {
        var schema = """{ "type": "integer", "maximum": 10 }""";

        Assert.Empty(await Validate(schema, "5", allowStringNumerics: false));
        Assert.NotEmpty(await Validate(schema, "11", allowStringNumerics: false));
    }

    [Fact]
    public async Task Option_on_rejects_string_too_large_for_int64()
    {
        var schema = """
            {
              "type": ["string", "integer"],
              "pattern": "^-?(?:0|[1-9]\\d*)$"
            }
            """;

        var errors = await Validate(schema, "\"99999999999999999999999999\"", allowStringNumerics: true);

        Assert.Contains(errors, e => e.Message.Contains("outside the representable range"));
    }

    [Fact]
    public async Task Option_on_rejects_negative_string_too_large_for_int64()
    {
        var schema = """
            {
              "type": ["string", "integer"],
              "pattern": "^-?(?:0|[1-9]\\d*)$"
            }
            """;

        var errors = await Validate(schema, "\"-99999999999999999999999999\"", allowStringNumerics: true);

        Assert.Contains(errors, e => e.Message.Contains("outside the representable range"));
    }

    [Fact]
    public async Task Option_on_rejects_number_string_beyond_double_range()
    {
        var schema = """
            {
              "type": ["string", "number"],
              "pattern": "^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?(?:[eE][+-]?\\d+)?$"
            }
            """;

        var errors = await Validate(schema, "\"1e5000\"", allowStringNumerics: true);

        Assert.Contains(errors, e => e.Message.Contains("outside the representable range"));
    }

    [Fact]
    public async Task Raw_numeric_overflow_triggers_maximum_error()
    {
        // Confirms that the same silent-pass concern does NOT apply to raw
        // (unquoted) JSON numbers. JsonDocument preserves the literal and the
        // schema evaluator enforces `maximum` on the parsed value.
        var schema = """{ "type": "integer", "maximum": 100 }""";

        Assert.NotEmpty(await Validate(schema, "99999999999999999999999999", allowStringNumerics: false));
        Assert.NotEmpty(await Validate(schema, "99999999999999999999999999", allowStringNumerics: true));
    }

    [Fact]
    public async Task Option_on_rejects_fractional_string_for_integer_via_pattern()
    {
        // Pattern rejects "3.14" for integer-shaped pattern, so the evaluator
        // produces a pattern error (no synthetic coercion error fires).
        var schema = """
            {
              "type": ["string", "integer"],
              "pattern": "^-?(?:0|[1-9]\\d*)$"
            }
            """;

        var errors = await Validate(schema, "\"3.14\"", allowStringNumerics: true);

        Assert.NotEmpty(errors);
        Assert.DoesNotContain(errors, e => e.Message.Contains("outside the representable range"));
    }

    [Fact]
    public async Task Option_on_keeps_string_schema_untouched()
    {
        var schema = """{ "type": "string", "minLength": 3 }""";

        Assert.Empty(await Validate(schema, "\"abc\"", allowStringNumerics: true));
        Assert.NotEmpty(await Validate(schema, "\"ab\"", allowStringNumerics: true));
    }

    private static async Task<IReadOnlyList<ValidationErrorResult.ValidationError>> Validate(
        string valueSchemaJson,
        string valueJson,
        bool allowStringNumerics)
    {
        var options = new SpecGuardOptions { AllowStringNumerics = allowStringNumerics };
        var validator = new JsonBodyValidator(options);
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
