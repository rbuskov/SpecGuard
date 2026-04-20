using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SpecGuard.Validators;
using SpecGuard.Validators.ValidationResults;

namespace SpecGuard.Test.Validators;

public class JsonBodyValidatorCompositionTests
{
    [Fact]
    public async Task AllOf_accepts_value_matching_every_branch()
    {
        var schema = """
            {
              "allOf": [
                { "type": "integer" },
                { "minimum": 10 },
                { "maximum": 20 }
              ]
            }
            """;

        Assert.Empty(await Validate(schema, "15"));
    }

    [Fact]
    public async Task AllOf_rejects_value_failing_any_branch()
    {
        var schema = """
            {
              "allOf": [
                { "type": "integer" },
                { "minimum": 10 }
              ]
            }
            """;

        Assert.NotEmpty(await Validate(schema, "5"));
        Assert.NotEmpty(await Validate(schema, "\"fifteen\""));
    }

    [Fact]
    public async Task AnyOf_accepts_when_at_least_one_branch_matches()
    {
        var schema = """
            {
              "anyOf": [
                { "type": "string" },
                { "type": "integer" }
              ]
            }
            """;

        Assert.Empty(await Validate(schema, "\"hello\""));
        Assert.Empty(await Validate(schema, "42"));
    }

    [Fact]
    public async Task AnyOf_rejects_when_no_branch_matches()
    {
        var schema = """
            {
              "anyOf": [
                { "type": "string" },
                { "type": "integer" }
              ]
            }
            """;

        Assert.NotEmpty(await Validate(schema, "3.14"));
        Assert.NotEmpty(await Validate(schema, "true"));
    }

    [Fact]
    public async Task OneOf_accepts_when_exactly_one_branch_matches()
    {
        // An integer matches {type:integer} but not {type:string}.
        var schema = """
            {
              "oneOf": [
                { "type": "string" },
                { "type": "integer" }
              ]
            }
            """;

        Assert.Empty(await Validate(schema, "42"));
        Assert.Empty(await Validate(schema, "\"hello\""));
    }

    [Fact]
    public async Task OneOf_rejects_when_zero_branches_match()
    {
        var schema = """
            {
              "oneOf": [
                { "type": "string" },
                { "type": "integer" }
              ]
            }
            """;

        Assert.NotEmpty(await Validate(schema, "true"));
    }

    [Fact]
    public async Task OneOf_rejects_when_multiple_branches_match()
    {
        // 10 satisfies both {minimum:5} and {maximum:20}, so two branches match.
        var schema = """
            {
              "oneOf": [
                { "type": "integer", "minimum": 5 },
                { "type": "integer", "maximum": 20 }
              ]
            }
            """;

        Assert.NotEmpty(await Validate(schema, "10"));
    }

    [Fact]
    public async Task Not_accepts_value_that_fails_the_sub_schema()
    {
        var schema = """{ "not": { "type": "string" } }""";

        Assert.Empty(await Validate(schema, "42"));
    }

    [Fact]
    public async Task Not_rejects_value_that_matches_the_sub_schema()
    {
        var schema = """{ "not": { "type": "string" } }""";

        Assert.NotEmpty(await Validate(schema, "\"hello\""));
    }

    [Fact]
    public async Task IfThen_applies_then_when_if_matches()
    {
        var schema = """
            {
              "if":   { "type": "integer" },
              "then": { "minimum": 100 }
            }
            """;

        // Integer >= 100: if matches, then passes.
        Assert.Empty(await Validate(schema, "150"));

        // Integer < 100: if matches, then fails.
        Assert.NotEmpty(await Validate(schema, "50"));

        // Non-integer: if doesn't match, then is skipped (no else).
        Assert.Empty(await Validate(schema, "\"anything\""));
    }

    [Fact]
    public async Task IfThenElse_applies_else_when_if_does_not_match()
    {
        var schema = """
            {
              "if":   { "type": "integer" },
              "then": { "minimum": 100 },
              "else": { "type": "string" }
            }
            """;

        Assert.Empty(await Validate(schema, "150"));
        Assert.NotEmpty(await Validate(schema, "50"));
        Assert.Empty(await Validate(schema, "\"hello\""));
        Assert.NotEmpty(await Validate(schema, "3.14"));
    }

    [Fact]
    public async Task If_without_then_or_else_is_a_noop()
    {
        // `if` alone (no `then`/`else`) never causes a failure.
        var schema = """{ "if": { "type": "integer" } }""";

        Assert.Empty(await Validate(schema, "42"));
        Assert.Empty(await Validate(schema, "\"hello\""));
    }

    [Fact]
    public async Task Nested_composition_of_allOf_inside_oneOf_inside_items()
    {
        // Exercises deep nesting per the spec's edge-case list.
        var schema = """
            {
              "type": "array",
              "items": {
                "oneOf": [
                  {
                    "allOf": [
                      { "type": "integer" },
                      { "minimum": 0 }
                    ]
                  },
                  { "type": "string" }
                ]
              }
            }
            """;

        Assert.Empty(
            await Validate(schema, """[1, "two", 3]"""));

        // -1 fails allOf branch (minimum 0) and also isn't a string -> zero matches.
        Assert.NotEmpty(await Validate(schema, """[-1]"""));
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
