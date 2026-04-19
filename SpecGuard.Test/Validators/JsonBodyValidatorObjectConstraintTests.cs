using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SpecGuard.Test.Validators;

public class JsonBodyValidatorObjectConstraintTests
{
    [Fact]
    public async Task Required_accepts_object_with_all_listed_keys()
    {
        var result = await Validate(
            """{ "type": "object", "required": ["name", "age"] }""",
            """{ "name": "Fido", "age": 3 }""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Required_rejects_object_missing_a_key()
    {
        var result = await Validate(
            """{ "type": "object", "required": ["name", "age"] }""",
            """{ "name": "Fido" }""");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Properties_validates_each_present_property()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string" },
                "age": { "type": "integer" }
              }
            }
            """;

        Assert.Empty(
            await Validate(schema, """{ "name": "Fido", "age": 3 }"""));

        Assert.NotEmpty(await Validate(schema, """{ "name": "Fido", "age": "three" }"""));
    }

    [Fact]
    public async Task Properties_ignores_keys_not_listed_when_no_additionalProperties()
    {
        // Without additionalProperties, unlisted keys pass through freely.
        var result = await Validate(
            """{ "type": "object", "properties": { "name": { "type": "string" } } }""",
            """{ "name": "Fido", "extra": 99 }""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task PatternProperties_validates_matching_keys()
    {
        var schema = """
            {
              "type": "object",
              "patternProperties": {
                "^S_": { "type": "string" }
              }
            }
            """;

        Assert.Empty(await Validate(schema, """{ "S_name": "Fido" }"""));
        Assert.NotEmpty(await Validate(schema, """{ "S_name": 42 }"""));
    }

    [Fact]
    public async Task PatternProperties_ignores_non_matching_keys()
    {
        var result = await Validate(
            """{ "type": "object", "patternProperties": { "^S_": { "type": "string" } } }""",
            """{ "other": 42 }""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task AdditionalProperties_false_rejects_unknown_keys()
    {
        var schema = """
            {
              "type": "object",
              "properties": { "name": { "type": "string" } },
              "additionalProperties": false
            }
            """;

        Assert.Empty(await Validate(schema, """{ "name": "Fido" }"""));
        Assert.NotEmpty(await Validate(schema, """{ "name": "Fido", "extra": 1 }"""));
    }

    [Fact]
    public async Task AdditionalProperties_schema_validates_extra_keys()
    {
        var schema = """
            {
              "type": "object",
              "properties": { "name": { "type": "string" } },
              "additionalProperties": { "type": "integer" }
            }
            """;

        Assert.Empty(
            await Validate(schema, """{ "name": "Fido", "age": 3 }"""));

        Assert.NotEmpty(await Validate(schema, """{ "name": "Fido", "age": "three" }"""));
    }

    [Fact]
    public async Task AdditionalProperties_does_not_apply_to_keys_covered_by_patternProperties()
    {
        var schema = """
            {
              "type": "object",
              "patternProperties": { "^S_": { "type": "string" } },
              "additionalProperties": false
            }
            """;

        // "S_name" is covered by patternProperties, so additionalProperties: false does not reject it.
        Assert.Empty(await Validate(schema, """{ "S_name": "Fido" }"""));
        Assert.NotEmpty(await Validate(schema, """{ "other": "Fido" }"""));
    }

    [Fact]
    public async Task MinProperties_rejects_too_few_keys()
    {
        var schema = """{ "type": "object", "minProperties": 2 }""";

        Assert.Empty(await Validate(schema, """{ "a": 1, "b": 2 }"""));
        Assert.NotEmpty(await Validate(schema, """{ "a": 1 }"""));
    }

    [Fact]
    public async Task MaxProperties_rejects_too_many_keys()
    {
        var schema = """{ "type": "object", "maxProperties": 2 }""";

        Assert.Empty(await Validate(schema, """{ "a": 1, "b": 2 }"""));
        Assert.NotEmpty(await Validate(schema, """{ "a": 1, "b": 2, "c": 3 }"""));
    }

    [Fact]
    public async Task DependentRequired_requires_companion_keys_when_trigger_present()
    {
        var schema = """
            {
              "type": "object",
              "dependentRequired": {
                "credit_card": ["billing_address"]
              }
            }
            """;

        // Trigger absent: no constraint.
        Assert.Empty(await Validate(schema, """{ "name": "Fido" }"""));

        // Trigger present with companion: ok.
        Assert.Empty(
            await Validate(schema, """{ "credit_card": "1234", "billing_address": "1 Main St" }"""));

        // Trigger present without companion: fail.
        Assert.NotEmpty(await Validate(schema, """{ "credit_card": "1234" }"""));
    }

    [Fact]
    public async Task DependentSchemas_applies_schema_when_trigger_present()
    {
        var schema = """
            {
              "type": "object",
              "dependentSchemas": {
                "credit_card": {
                  "required": ["billing_address"]
                }
              }
            }
            """;

        Assert.Empty(await Validate(schema, """{ "name": "Fido" }"""));
        Assert.Empty(
            await Validate(schema, """{ "credit_card": "1234", "billing_address": "1 Main St" }"""));
        Assert.NotEmpty(await Validate(schema, """{ "credit_card": "1234" }"""));
    }

    [Fact]
    public async Task PropertyNames_validates_every_key()
    {
        var schema = """
            {
              "type": "object",
              "propertyNames": { "pattern": "^[a-z]+$" }
            }
            """;

        Assert.Empty(await Validate(schema, """{ "name": 1, "age": 2 }"""));
        Assert.NotEmpty(await Validate(schema, """{ "name": 1, "Age": 2 }"""));
    }

    [Fact]
    public async Task Type_object_rejects_array()
    {
        var result = await Validate("""{ "type": "object" }""", "[1, 2, 3]");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Empty_object_is_accepted_against_unconstrained_schema()
    {
        var result = await Validate("""{ "type": "object" }""", "{}");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Nested_object_properties_validate_recursively()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "owner": {
                  "type": "object",
                  "required": ["name"],
                  "properties": {
                    "name": { "type": "string" }
                  }
                }
              }
            }
            """;

        Assert.Empty(
            await Validate(schema, """{ "owner": { "name": "Alice" } }"""));

        Assert.NotEmpty(await Validate(schema, """{ "owner": { "name": 42 } }"""));
        Assert.NotEmpty(await Validate(schema, """{ "owner": {} }"""));
    }

    [Fact]
    public async Task RejectAdditionalProperties_rejects_unknown_fields()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string" }
              }
            }
            """;

        Assert.NotEmpty(await ValidateDirect(schema, """{ "name": "Alice", "extra": 1 }""", rejectAdditionalProperties: true));
    }

    [Fact]
    public async Task RejectAdditionalProperties_allows_known_fields()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string" }
              }
            }
            """;

        Assert.Empty(
            await ValidateDirect(schema, """{ "name": "Alice" }""", rejectAdditionalProperties: true));
    }

    [Fact]
    public async Task RejectAdditionalProperties_respects_explicit_true()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string" }
              },
              "additionalProperties": true
            }
            """;

        Assert.Empty(
            await ValidateDirect(schema, """{ "name": "Alice", "extra": 1 }""", rejectAdditionalProperties: true));
    }

    [Fact]
    public async Task RejectAdditionalProperties_applies_to_nested_objects()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "child": {
                  "type": "object",
                  "properties": {
                    "age": { "type": "integer" }
                  }
                }
              }
            }
            """;

        Assert.NotEmpty(await ValidateDirect(schema, """{ "child": { "age": 5, "extra": true } }""", rejectAdditionalProperties: true));
    }

    [Fact]
    public async Task RejectAdditionalProperties_does_not_affect_schemas_without_properties()
    {
        var schema = """{ "type": "object" }""";

        Assert.Empty(
            await ValidateDirect(schema, """{ "anything": 1 }""", rejectAdditionalProperties: true));
    }

    [Fact]
    public async Task RejectAdditionalProperties_default_allows_extra_fields()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string" }
              }
            }
            """;

        Assert.Empty(
            await ValidateDirect(schema, """{ "name": "Alice", "extra": 1 }""", rejectAdditionalProperties: false));
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

    private static async Task<IReadOnlyList<ValidationErrorResult.ValidationError>> ValidateDirect(string bodySchemaJson, string body, bool rejectAdditionalProperties)
    {
        var options = new SpecGuardOptions { RejectAdditionalProperties = rejectAdditionalProperties };
        var validator = new JsonBodyValidator(options);
        using var spec = BuildDirectSpec(bodySchemaJson);
        validator.Initialize(spec);

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

    private static JsonDocument BuildDirectSpec(string bodySchemaJson) => JsonDocument.Parse($$"""
        {
          "paths": {
            "/items": {
              "post": {
                "requestBody": {
                  "content": {
                    "application/json": {
                      "schema": {{bodySchemaJson}}
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
