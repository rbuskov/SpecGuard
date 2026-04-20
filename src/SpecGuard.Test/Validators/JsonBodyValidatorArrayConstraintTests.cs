using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SpecGuard.Validators;
using SpecGuard.Validators.ValidationResults;

namespace SpecGuard.Test.Validators;

public class JsonBodyValidatorArrayConstraintTests
{
    [Fact]
    public async Task MinItems_accepts_array_at_minimum()
    {
        var result = await Validate("""{ "type": "array", "minItems": 2 }""", "[1, 2]");

        Assert.Empty(result);
    }

    [Fact]
    public async Task MinItems_rejects_array_below_minimum()
    {
        var result = await Validate("""{ "type": "array", "minItems": 2 }""", "[1]");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task MaxItems_accepts_array_at_maximum()
    {
        var result = await Validate("""{ "type": "array", "maxItems": 2 }""", "[1, 2]");

        Assert.Empty(result);
    }

    [Fact]
    public async Task MaxItems_rejects_array_above_maximum()
    {
        var result = await Validate("""{ "type": "array", "maxItems": 2 }""", "[1, 2, 3]");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task UniqueItems_accepts_distinct_primitives()
    {
        var result = await Validate("""{ "type": "array", "uniqueItems": true }""", "[1, 2, 3]");

        Assert.Empty(result);
    }

    [Fact]
    public async Task UniqueItems_rejects_duplicate_primitives()
    {
        var result = await Validate("""{ "type": "array", "uniqueItems": true }""", "[1, 2, 1]");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task UniqueItems_uses_deep_equality_for_objects()
    {
        var result = await Validate(
            """{ "type": "array", "uniqueItems": true }""",
            """[{ "a": 1 }, { "a": 1 }]""");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task UniqueItems_treats_objects_with_different_values_as_distinct()
    {
        var result = await Validate(
            """{ "type": "array", "uniqueItems": true }""",
            """[{ "a": 1 }, { "a": 2 }]""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task UniqueItems_false_allows_duplicates()
    {
        var result = await Validate("""{ "type": "array", "uniqueItems": false }""", "[1, 1, 1]");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Items_validates_every_element()
    {
        var result = await Validate(
            """{ "type": "array", "items": { "type": "integer" } }""",
            "[1, 2, 3]");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Items_rejects_when_any_element_fails()
    {
        var result = await Validate(
            """{ "type": "array", "items": { "type": "integer" } }""",
            """[1, "two", 3]""");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task PrefixItems_validates_positional_schemas()
    {
        var result = await Validate(
            """{ "type": "array", "prefixItems": [ { "type": "string" }, { "type": "integer" } ] }""",
            """["hello", 42]""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task PrefixItems_rejects_when_positional_type_mismatches()
    {
        var result = await Validate(
            """{ "type": "array", "prefixItems": [ { "type": "string" }, { "type": "integer" } ] }""",
            """[42, "hello"]""");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task PrefixItems_allows_extra_elements_by_default()
    {
        // Without `items: false`, elements beyond prefixItems are unconstrained.
        var result = await Validate(
            """{ "type": "array", "prefixItems": [ { "type": "string" } ] }""",
            """["hello", 1, 2, 3]""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task PrefixItems_with_items_false_rejects_extra_elements()
    {
        var result = await Validate(
            """{ "type": "array", "prefixItems": [ { "type": "string" } ], "items": false }""",
            """["hello", "extra"]""");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Contains_accepts_array_with_matching_element()
    {
        var result = await Validate(
            """{ "type": "array", "contains": { "type": "integer" } }""",
            """["a", 1, "b"]""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Contains_rejects_array_with_no_matching_element()
    {
        var result = await Validate(
            """{ "type": "array", "contains": { "type": "integer" } }""",
            """["a", "b", "c"]""");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task MinContains_enforces_minimum_matching_count()
    {
        var schema = """
            {
              "type": "array",
              "contains": { "type": "integer" },
              "minContains": 2
            }
            """;

        Assert.Empty(await Validate(schema, """["a", 1, 2]"""));
        Assert.NotEmpty(await Validate(schema, """["a", 1, "b"]"""));
    }

    [Fact]
    public async Task MaxContains_enforces_maximum_matching_count()
    {
        var schema = """
            {
              "type": "array",
              "contains": { "type": "integer" },
              "maxContains": 2
            }
            """;

        Assert.Empty(await Validate(schema, """[1, 2, "a"]"""));
        Assert.NotEmpty(await Validate(schema, """[1, 2, 3]"""));
    }

    [Fact]
    public async Task MinContains_zero_allows_no_matches()
    {
        // minContains: 0 disables the "at least one" requirement of contains.
        var schema = """
            {
              "type": "array",
              "contains": { "type": "integer" },
              "minContains": 0
            }
            """;

        var result = await Validate(schema, """["a", "b"]""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Type_array_rejects_non_array()
    {
        var result = await Validate("""{ "type": "array" }""", """{ "not": "an array" }""");

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
