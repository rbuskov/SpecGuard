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
    public async Task Option_on_coerces_through_ref_to_numeric_component()
    {
        const string spec = """
            {
              "paths": {
                "/items": {
                  "post": {
                    "requestBody": {
                      "content": {
                        "application/json": {
                          "schema": {
                            "type": "object",
                            "properties": { "value": { "$ref": "#/components/schemas/Count" } }
                          }
                        }
                      }
                    }
                  }
                }
              },
              "components": {
                "schemas": {
                  "Count": {
                    "type": ["string","integer"],
                    "pattern": "^-?(?:0|[1-9]\\d*)$",
                    "maximum": 100
                  }
                }
              }
            }
            """;

        var options = new SpecGuardOptions { AllowStringNumerics = true };
        var validator = new JsonBodyValidator(options);
        using var specDoc = JsonDocument.Parse(spec);
        validator.Initialize(specDoc);

        var ctx = BuildContext("""{"value":"42"}""", "/items");
        Assert.Empty(await validator.ValidateAsync(ctx, CancellationToken.None));

        var badCtx = BuildContext("""{"value":"101"}""", "/items");
        Assert.NotEmpty(await validator.ValidateAsync(badCtx, CancellationToken.None));
    }

    [Fact]
    public async Task Option_on_coerces_object_properties_through_allOf_composition()
    {
        // allOf coercion only activates on object-valued instances — walking
        // the composition branches from WalkObject. A child property declared
        // in one of those branches is coerced.
        var schema = """
            {
              "type": "object",
              "allOf": [
                {
                  "properties": {
                    "count": { "type": ["string","integer"], "pattern": "^-?(?:0|[1-9]\\d*)$", "maximum": 10 }
                  }
                }
              ]
            }
            """;

        Assert.Empty(await Validate(schema, """{"count":"5"}""", allowStringNumerics: true));
        Assert.NotEmpty(await Validate(schema, """{"count":"99"}""", allowStringNumerics: true));
    }

    [Fact]
    public async Task Option_on_does_not_coerce_through_non_nullable_oneOf_branches()
    {
        // Non-nullable oneOf: coercion does NOT descend into branches. A
        // numeric-typed branch therefore never sees a coerced number —
        // `maximum` is not enforced through oneOf, locking the limitation.
        var schema = """
            {
              "oneOf": [
                { "type": "array" },
                { "type": ["string","integer"], "pattern": "^-?(?:0|[1-9]\\d*)$", "maximum": 10 }
              ]
            }
            """;

        // "100" satisfies branch 2 as a string (pattern matches), but maximum
        // does not apply to the un-coerced string. Result: exactly one
        // branch matches → VALID despite the numeric value being out of range.
        Assert.Empty(await Validate(schema, "\"100\"", allowStringNumerics: true));
    }

    [Fact]
    public async Task Option_on_coerces_nested_arrays_of_numeric_strings()
    {
        var schema = """
            {
              "type": "array",
              "items": {
                "type": "array",
                "items": {
                  "type": ["string","integer"],
                  "pattern": "^-?(?:0|[1-9]\\d*)$",
                  "maximum": 10
                }
              }
            }
            """;

        Assert.Empty(await Validate(schema, "[[\"1\",\"2\"],[\"3\"]]", allowStringNumerics: true));
        Assert.NotEmpty(await Validate(schema, "[[\"1\",\"2\"],[\"99\"]]", allowStringNumerics: true));
    }

    [Fact]
    public async Task Option_on_pattern_absent_still_surfaces_out_of_range()
    {
        // No `pattern` on the schema, but NumericShape fallback recognizes
        // a numeric-shaped string that overflows the representable range.
        var schema = """
            {
              "type": ["string","integer"]
            }
            """;

        var errors = await Validate(schema, "\"99999999999999999999999999\"", allowStringNumerics: true);
        Assert.Contains(errors, e => e.Message.Contains("outside the representable range"));
    }

    [Fact]
    public async Task Option_on_integer_type_only_uses_integer_in_error_message()
    {
        var schema = """
            {
              "type": ["string","integer"],
              "pattern": "^-?(?:0|[1-9]\\d*)$"
            }
            """;

        var errors = await Validate(schema, "\"99999999999999999999999999\"", allowStringNumerics: true);

        Assert.Contains(errors, e => e.Message.Contains("integer"));
    }

    [Fact]
    public async Task Option_on_rejects_hex_shaped_string_via_pattern()
    {
        var schema = """
            {
              "type": ["string","integer"],
              "pattern": "^-?(?:0|[1-9]\\d*)$"
            }
            """;

        // "0x1F" does not satisfy the integer-shape regex and no coercion
        // error is synthesized; the pattern failure stands as the message.
        var errors = await Validate(schema, "\"0x1F\"", allowStringNumerics: true);

        Assert.NotEmpty(errors);
        Assert.DoesNotContain(errors, e => e.Message.Contains("outside the representable range"));
    }

    [Fact]
    public async Task Option_on_coerces_long_maxvalue_boundary()
    {
        var schema = """
            {
              "type": ["string","integer"],
              "pattern": "^-?(?:0|[1-9]\\d*)$"
            }
            """;

        // At long.MaxValue coercion succeeds via long.TryParse.
        Assert.Empty(await Validate(schema, $"\"{long.MaxValue}\"", allowStringNumerics: true));

        // long.MaxValue + 1 fits into ulong and coerces via ulong.TryParse.
        Assert.Empty(await Validate(schema, "\"9223372036854775808\"", allowStringNumerics: true));

        // ulong.MaxValue + 1 overflows both — out-of-range error.
        var errors = await Validate(schema, "\"18446744073709551616\"", allowStringNumerics: true);
        Assert.Contains(errors, e => e.Message.Contains("outside the representable range"));
    }

    [Fact]
    public async Task Option_on_coerces_negative_long_value()
    {
        var schema = """
            {
              "type": ["string","integer"],
              "pattern": "^-?(?:0|[1-9]\\d*)$",
              "minimum": -100,
              "maximum": 100
            }
            """;

        // Negative long inside the acceptable range → accepted.
        Assert.Empty(await Validate(schema, "\"-50\"", allowStringNumerics: true));
        // Below minimum → rejected (after coercion to long).
        Assert.NotEmpty(await Validate(schema, "\"-200\"", allowStringNumerics: true));
    }

    [Fact]
    public async Task Option_on_fractional_zero_against_integer_pattern_fails()
    {
        var schema = """
            {
              "type": ["string","integer"],
              "pattern": "^-?(?:0|[1-9]\\d*)$"
            }
            """;

        // "1.0" doesn't satisfy the integer-shape regex — pattern error,
        // no synthetic out-of-range error.
        var errors = await Validate(schema, "\"1.0\"", allowStringNumerics: true);

        Assert.NotEmpty(errors);
        Assert.DoesNotContain(errors, e => e.Message.Contains("outside the representable range"));
    }

    [Fact]
    public async Task Coercion_and_schema_errors_appear_in_the_same_response()
    {
        // A body where one property coercion overflows AND another property
        // fails a separate schema check: both errors reach the final list.
        var schema = """
            {
              "type": "object",
              "properties": {
                "big": {
                  "type": ["string","integer"],
                  "pattern": "^-?(?:0|[1-9]\\d*)$"
                },
                "name": { "type": "string", "minLength": 3 }
              }
            }
            """;

        var errors = await ValidateDirect(
            schema, """{"big":"99999999999999999999999999","name":"x"}""");

        Assert.Contains(errors, e => e.Message.Contains("outside the representable range"));
        Assert.Contains(errors, e => e.Path == "/name");
    }

    [Fact]
    public async Task Option_on_does_not_unwrap_nullable_anyOf_for_coercion()
    {
        // Nullable unwrap is special-cased only for `oneOf`. An equivalent
        // shape under `anyOf` is NOT recognized — coercion does not see
        // the inner numeric type. Lock the limitation.
        var schema = """
            {
              "anyOf": [
                { "type": "null" },
                {
                  "type": ["string","integer"],
                  "pattern": "^-?(?:0|[1-9]\\d*)$",
                  "maximum": 10
                }
              ]
            }
            """;

        // "11" is a string that satisfies the second branch's pattern but
        // exceeds maximum — without coercion, maximum doesn't fire.
        Assert.Empty(await Validate(schema, "\"11\"", allowStringNumerics: true));
    }

    [Fact]
    public async Task Option_on_throwaway_pattern_still_emits_out_of_range()
    {
        // Schema has a throwaway pattern (matches anything) and an integer
        // type union. NumericShape fallback kicks in to recognize the
        // numeric shape and emit the out-of-range error.
        var schema = """
            {
              "type": ["string","integer"],
              "pattern": ".*"
            }
            """;

        var errors = await Validate(schema, "\"99999999999999999999999999\"", allowStringNumerics: true);

        Assert.Contains(errors, e => e.Message.Contains("outside the representable range"));
    }

    [Fact]
    public void Malformed_pattern_in_spec_throws_at_initialization()
    {
        // The underlying Json.Schema library validates regex patterns at
        // schema-build time and throws RegexParseException. This means a
        // malformed pattern in the spec is a startup-time failure, not a
        // request-time fallback. Lock the behavior so a future change that
        // tries to defer pattern validation gets caught.
        var options = new SpecGuardOptions { AllowStringNumerics = true };
        var validator = new JsonBodyValidator(options);
        using var spec = JsonDocument.Parse("""
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
                              "value": { "type": ["string","integer"], "pattern": "(unclosed" }
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

        Assert.ThrowsAny<Exception>(() => validator.Initialize(spec));
    }

    [Fact]
    public async Task Option_on_infinity_string_is_treated_as_out_of_range()
    {
        // "Infinity" parses via double.TryParse but is filtered out by
        // !double.IsInfinity check. Falls through to LooksNumericForSchema
        // — the default NumericShape rejects "Infinity", so no synthetic
        // error fires. The schema evaluator's pattern check still rejects
        // it though.
        var schema = """
            {
              "type": ["string","number"],
              "pattern": "^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$"
            }
            """;

        var errors = await Validate(schema, "\"Infinity\"", allowStringNumerics: true);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public async Task Coercion_errors_have_body_in_and_correct_pointer_path()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "nested": {
                  "type": "object",
                  "properties": {
                    "v": {
                      "type": ["string","integer"],
                      "pattern": "^-?(?:0|[1-9]\\d*)$"
                    }
                  }
                }
              }
            }
            """;

        var errors = await ValidateDirect(
            schema, """{"nested":{"v":"999999999999999999999999"}}""");

        var coercion = Assert.Single(errors, e => e.Message.Contains("outside the representable range"));
        Assert.Equal("body", coercion.In);
        Assert.Equal("/nested/v", coercion.Path);
    }

    [Fact]
    public async Task Option_on_keeps_string_schema_untouched()
    {
        var schema = """{ "type": "string", "minLength": 3 }""";

        Assert.Empty(await Validate(schema, "\"abc\"", allowStringNumerics: true));
        Assert.NotEmpty(await Validate(schema, "\"ab\"", allowStringNumerics: true));
    }

    private static async Task<IReadOnlyList<ValidationErrorResult.ValidationError>> ValidateDirect(
        string bodySchemaJson, string body)
    {
        var options = new SpecGuardOptions { AllowStringNumerics = true };
        var validator = new JsonBodyValidator(options);
        using var spec = JsonDocument.Parse($$"""
            {
              "paths": {
                "/items": {
                  "post": {
                    "requestBody": {
                      "content": {
                        "application/json": { "schema": {{bodySchemaJson}} }
                      }
                    }
                  }
                }
              }
            }
            """);
        validator.Initialize(spec);

        return await validator.ValidateAsync(BuildContext(body, "/items"), CancellationToken.None);
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
