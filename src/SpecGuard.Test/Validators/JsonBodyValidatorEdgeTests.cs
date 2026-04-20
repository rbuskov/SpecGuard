using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SpecGuard.Test.Validators;

public class JsonBodyValidatorEdgeTests
{
    // ── Operation + content-type matching ────────────────────────────────

    [Fact]
    public async Task Operation_with_only_xml_content_is_not_matched_as_json()
    {
        const string spec = """
            {
              "paths": {
                "/pets": {
                  "post": {
                    "requestBody": {
                      "required": true,
                      "content": {
                        "application/xml": { "schema": { "type": "object" } }
                      }
                    }
                  }
                }
              }
            }
            """;
        var validator = new JsonBodyValidator();
        using var doc = JsonDocument.Parse(spec);
        validator.Initialize(doc);

        // When the validator's schema resolver can't find application/json,
        // BodyRequired falls back to false and MatchesOperation matches only
        // on the route+method. Assert the "required body missing" error does
        // not fire for an XML-only operation.
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.Path = new PathString("/pets");

        Assert.True(validator.MatchesOperation(ctx.Request));

        // No ParsedBody + no BodyEmpty set (non-JSON content type) → empty errors.
        var result = await validator.ValidateAsync(ctx, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Operation_with_requestBody_but_no_content_is_skipped()
    {
        const string spec = """
            {
              "paths": {
                "/pets": {
                  "post": {
                    "requestBody": { "required": true }
                  }
                }
              }
            }
            """;
        var validator = new JsonBodyValidator();
        using var doc = JsonDocument.Parse(spec);
        validator.Initialize(doc);

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.Path = new PathString("/pets");

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Operation_with_json_content_but_no_schema_is_skipped()
    {
        const string spec = """
            {
              "paths": {
                "/pets": {
                  "post": {
                    "requestBody": {
                      "content": {
                        "application/json": { }
                      }
                    }
                  }
                }
              }
            }
            """;
        var validator = new JsonBodyValidator();
        using var doc = JsonDocument.Parse(spec);
        validator.Initialize(doc);

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.Path = new PathString("/pets");
        ctx.Items["SpecGuard.ParsedBody"] = JsonDocument.Parse("""{"anything":true}""").RootElement;

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);
        Assert.Empty(result);
    }

    // ── Body shape ────────────────────────────────────────────────────────

    [Fact]
    public async Task Top_level_array_is_validated_against_array_schema()
    {
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec("""{ "type": "array", "items": { "type": "integer" } }""");
        validator.Initialize(spec);

        var okCtx = BuildContext("[1,2,3]");
        Assert.Empty(await validator.ValidateAsync(okCtx, CancellationToken.None));

        var badCtx = BuildContext("[1,\"oops\",3]");
        Assert.NotEmpty(await validator.ValidateAsync(badCtx, CancellationToken.None));
    }

    [Fact]
    public async Task Top_level_scalar_is_validated_against_string_schema()
    {
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec("""{ "type": "string", "enum": ["X","O"] }""");
        validator.Initialize(spec);

        Assert.Empty(await validator.ValidateAsync(BuildContext("\"X\""), CancellationToken.None));
        Assert.NotEmpty(await validator.ValidateAsync(BuildContext("\"Z\""), CancellationToken.None));
    }

    [Fact]
    public async Task Null_body_against_nullable_object_is_accepted()
    {
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec("""{ "type": ["object","null"] }""");
        validator.Initialize(spec);

        var ctx = BuildContext("null");
        Assert.Empty(await validator.ValidateAsync(ctx, CancellationToken.None));
    }

    [Fact]
    public async Task Null_body_against_object_only_schema_is_rejected()
    {
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec("""{ "type": "object" }""");
        validator.Initialize(spec);

        var ctx = BuildContext("null");
        Assert.NotEmpty(await validator.ValidateAsync(ctx, CancellationToken.None));
    }

    [Fact]
    public async Task Empty_array_body_against_object_schema_is_rejected()
    {
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec("""{ "type": "object" }""");
        validator.Initialize(spec);

        var result = await validator.ValidateAsync(BuildContext("[]"), CancellationToken.None);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Required_true_with_empty_content_object_is_treated_as_no_json_body()
    {
        const string spec = """
            {
              "paths": {
                "/items": {
                  "post": {
                    "requestBody": { "required": true, "content": {} }
                  }
                }
              }
            }
            """;
        var validator = new JsonBodyValidator();
        using var doc = JsonDocument.Parse(spec);
        validator.Initialize(doc);

        // No application/json content registered, so BodyRequired falls back
        // to false and an empty request is accepted.
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.Path = new PathString("/items");

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadOnly_on_reused_component_is_collected_at_both_locations()
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
                            "properties": {
                              "first":  { "$ref": "#/components/schemas/Entity" },
                              "second": { "$ref": "#/components/schemas/Entity" }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              },
              "components": {
                "schemas": {
                  "Entity": {
                    "type": "object",
                    "properties": {
                      "id":   { "type": "integer", "readOnly": true },
                      "name": { "type": "string" }
                    }
                  }
                }
              }
            }
            """;
        var validator = new JsonBodyValidator();
        using var doc = JsonDocument.Parse(spec);
        validator.Initialize(doc);

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.Path = new PathString("/items");
        ctx.Items["SpecGuard.ParsedBody"] = JsonDocument
            .Parse("""{"first":{"id":1,"name":"a"},"second":{"id":2,"name":"b"}}""")
            .RootElement;

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);

        Assert.Contains(result, e => e.Path == "/first/id");
        Assert.Contains(result, e => e.Path == "/second/id");
    }

    [Fact]
    public async Task RejectAdditionalProperties_does_not_override_patternProperties()
    {
        // When a schema declares `patternProperties` but no explicit
        // `additionalProperties`, the global option injects
        // `additionalProperties: false`. Keys covered by patternProperties
        // are still accepted; non-matching keys are rejected.
        var options = new SpecGuardOptions { RejectAdditionalProperties = true };
        var validator = new JsonBodyValidator(options);
        using var spec = BuildSpec("""
            {
              "type": "object",
              "properties": { "name": { "type": "string" } },
              "patternProperties": { "^S_": { "type": "string" } }
            }
            """);
        validator.Initialize(spec);

        Assert.Empty(await validator.ValidateAsync(
            BuildContext("""{"name":"x","S_a":"y"}"""), CancellationToken.None));
        Assert.NotEmpty(await validator.ValidateAsync(
            BuildContext("""{"name":"x","unknown":"y"}"""), CancellationToken.None));
    }

    // ── readOnly through composition ──────────────────────────────────────

    [Fact]
    public async Task ReadOnly_collected_through_oneOf_branches_is_rejected()
    {
        var schema = """
            {
              "oneOf": [
                {
                  "type": "object",
                  "properties": { "id": { "type": "integer", "readOnly": true }, "kind": { "const": "A" } }
                },
                {
                  "type": "object",
                  "properties": { "kind": { "const": "B" } }
                }
              ]
            }
            """;
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec(schema);
        validator.Initialize(spec);

        var result = await validator.ValidateAsync(
            BuildContext("""{"id": 5, "kind": "A"}"""), CancellationToken.None);

        Assert.Contains(result, e => e.Path == "/id" && e.Message.Contains("read-only"));
    }

    [Fact]
    public async Task ReadOnly_collected_through_anyOf_branches_is_rejected()
    {
        var schema = """
            {
              "anyOf": [
                {
                  "type": "object",
                  "properties": { "id": { "type": "integer", "readOnly": true } }
                }
              ]
            }
            """;
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec(schema);
        validator.Initialize(spec);

        var result = await validator.ValidateAsync(
            BuildContext("""{"id": 5}"""), CancellationToken.None);

        Assert.Contains(result, e => e.Path == "/id" && e.Message.Contains("read-only"));
    }

    [Fact]
    public async Task All_required_fields_readOnly_strips_required_entirely()
    {
        // When every required field is readOnly, the built schema has no
        // `required` list. A request missing all of them therefore passes.
        var schema = """
            {
              "type": "object",
              "required": ["id","createdAt"],
              "properties": {
                "id":        { "type": "integer", "readOnly": true },
                "createdAt": { "type": "string",  "readOnly": true }
              }
            }
            """;
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec(schema);
        validator.Initialize(spec);

        Assert.Empty(await validator.ValidateAsync(BuildContext("{}"), CancellationToken.None));
    }

    [Fact]
    public async Task ReadOnly_array_property_is_rejected_as_whole()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "tags": { "type": "array", "items": { "type": "string" }, "readOnly": true }
              }
            }
            """;
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec(schema);
        validator.Initialize(spec);

        var result = await validator.ValidateAsync(
            BuildContext("""{"tags":["x"]}"""), CancellationToken.None);

        Assert.Contains(result, e => e.Path == "/tags" && e.Message.Contains("read-only"));
    }

    // ── Required body edge cases ──────────────────────────────────────────

    [Fact]
    public async Task Body_null_with_object_type_is_rejected_cleanly()
    {
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec("""{ "type": "object" }""");
        validator.Initialize(spec);

        var ctx = BuildContext("null");
        var result = await validator.ValidateAsync(ctx, CancellationToken.None);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Const_object_mismatch_produces_non_empty_error_message()
    {
        var schema = """{ "const": { "a": 1, "b": [2, 3] } }""";
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec(schema);
        validator.Initialize(spec);

        var result = await validator.ValidateAsync(
            BuildContext("""{"a":1,"b":[2,4]}"""), CancellationToken.None);

        Assert.NotEmpty(result);
        Assert.All(result, e => Assert.False(string.IsNullOrEmpty(e.Message)));
    }

    [Fact]
    public async Task Array_index_evaluation_path_falls_back_to_preceding_keyword()
    {
        // An error whose evaluation path ends with a numeric segment
        // (array index) should still produce a non-empty, well-formed
        // error message via the LastEvaluationKeyword fallback.
        var schema = """
            {
              "type": "array",
              "items": { "not": { "type": "string" } }
            }
            """;
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec(schema);
        validator.Initialize(spec);

        var result = await validator.ValidateAsync(
            BuildContext("""["hello"]"""), CancellationToken.None);

        Assert.NotEmpty(result);
        Assert.All(result, e => Assert.False(string.IsNullOrEmpty(e.Message)));
    }

    // ── Fallback error synthesis ──────────────────────────────────────────

    [Fact]
    public async Task OneOf_multiple_match_synthesizes_error_with_keyword()
    {
        // Both integer branches match 10, so oneOf fails "exactly one".
        var schema = """
            {
              "oneOf": [
                { "type": "integer", "minimum": 5 },
                { "type": "integer", "maximum": 20 }
              ]
            }
            """;
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec(schema);
        validator.Initialize(spec);

        var result = await validator.ValidateAsync(BuildContext("10"), CancellationToken.None);

        Assert.NotEmpty(result);
        Assert.All(result, e => Assert.False(string.IsNullOrEmpty(e.Message)));
    }

    [Fact]
    public async Task Not_keyword_failure_synthesizes_error()
    {
        var schema = """{ "not": { "type": "string" } }""";
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec(schema);
        validator.Initialize(spec);

        var result = await validator.ValidateAsync(
            BuildContext("\"hello\""), CancellationToken.None);

        Assert.NotEmpty(result);
        Assert.All(result, e => Assert.False(string.IsNullOrEmpty(e.Message)));
    }

    // ── Combined options ──────────────────────────────────────────────────

    [Fact]
    public async Task RejectAdditionalProperties_and_AllowStringNumerics_together()
    {
        var options = new SpecGuardOptions
        {
            RejectAdditionalProperties = true,
            AllowStringNumerics = true,
        };
        var validator = new JsonBodyValidator(options);
        using var spec = BuildSpec("""
            {
              "type": "object",
              "properties": {
                "count": { "type": ["string","integer"], "pattern": "^-?(?:0|[1-9]\\d*)$", "maximum": 100 }
              }
            }
            """);
        validator.Initialize(spec);

        // String numeric is coerced AND in range.
        Assert.Empty(await validator.ValidateAsync(
            BuildContext("""{"count":"42"}"""), CancellationToken.None));

        // Unknown property rejected by RejectAdditionalProperties.
        Assert.NotEmpty(await validator.ValidateAsync(
            BuildContext("""{"count":"42","extra":1}"""), CancellationToken.None));

        // String numeric over maximum is rejected by AllowStringNumerics evaluation.
        Assert.NotEmpty(await validator.ValidateAsync(
            BuildContext("""{"count":"101"}"""), CancellationToken.None));
    }

    private static JsonDocument BuildSpec(string bodySchema) => JsonDocument.Parse($$"""
        {
          "paths": {
            "/items": {
              "post": {
                "requestBody": {
                  "content": {
                    "application/json": { "schema": {{bodySchema}} }
                  }
                }
              }
            }
          }
        }
        """);

    private static DefaultHttpContext BuildContext(string body)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.Path = new PathString("/items");
        ctx.Items["SpecGuard.ParsedBody"] = JsonDocument.Parse(body).RootElement;
        return ctx;
    }
}
