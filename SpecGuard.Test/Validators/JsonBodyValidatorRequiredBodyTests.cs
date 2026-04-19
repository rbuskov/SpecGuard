using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SpecGuard.Test.Validators;

public class JsonBodyValidatorRequiredBodyTests
{
    [Fact]
    public async Task Required_body_present_is_accepted()
    {
        var result = await Validate(required: true, hasJsonBody: true, body: """{"name":"Fido"}""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Required_body_missing_is_rejected()
    {
        var result = await Validate(required: true, hasJsonBody: true, body: null, bodyEmpty: true);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Required_body_whitespace_only_is_rejected()
    {
        var result = await Validate(required: true, hasJsonBody: true, body: null, bodyEmpty: true);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Required_body_no_content_type_is_rejected()
    {
        // No items set = middleware didn't parse (no JSON content type)
        var result = await Validate(required: true, hasJsonBody: false, body: null);

        Assert.NotEmpty(result);
        var error = Assert.Single(result);
        Assert.Equal("body", error.In);
        Assert.Contains("required", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Optional_body_missing_is_accepted()
    {
        // No items set = middleware didn't parse (no JSON content type)
        var result = await Validate(required: false, hasJsonBody: false, body: null);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Optional_body_empty_with_json_content_type_is_accepted()
    {
        var result = await Validate(required: false, hasJsonBody: true, body: null, bodyEmpty: true);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Optional_body_whitespace_with_json_content_type_is_accepted()
    {
        var result = await Validate(required: false, hasJsonBody: true, body: null, bodyEmpty: true);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Required_body_empty_with_json_content_type_returns_missing()
    {
        var result = await Validate(required: true, hasJsonBody: true, body: null, bodyEmpty: true);

        Assert.NotEmpty(result);
        var error = Assert.Single(result);
        Assert.Equal("body", error.In);
        Assert.Contains("required", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Optional_body_present_and_valid_is_accepted()
    {
        var result = await Validate(required: false, hasJsonBody: true, body: """{"name":"Fido"}""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Default_required_unspecified_missing_body_is_accepted()
    {
        // When `required` is not specified on requestBody, it defaults to false.
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec(requiredClause: null);
        validator.Initialize(spec);

        // No items set = middleware didn't parse (no JSON content type)
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = new PathString("/pet");

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Required_body_that_is_empty_object_is_accepted_when_schema_allows()
    {
        // "non-empty" in the spec means the body is present, not that it has properties.
        // An empty JSON object is still a present, parseable body.
        var result = await Validate(required: true, hasJsonBody: true, body: "{}");

        Assert.Empty(result);
    }

    private static async Task<IReadOnlyList<ValidationErrorResult.ValidationError>> Validate(
        bool required, bool hasJsonBody, string? body, bool bodyEmpty = false)
    {
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec(requiredClause: $"\"required\": {(required ? "true" : "false")},");
        validator.Initialize(spec);

        var context = BuildContext(hasJsonBody, body, bodyEmpty);
        return await validator.ValidateAsync(context, CancellationToken.None);
    }

    private static JsonDocument BuildSpec(string? requiredClause) => JsonDocument.Parse($$"""
        {
          "paths": {
            "/pet": {
              "post": {
                "requestBody": {
                  {{requiredClause}}
                  "content": {
                    "application/json": {
                      "schema": {
                        "type": "object",
                        "properties": {
                          "name": { "type": "string" }
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

    private static DefaultHttpContext BuildContext(bool hasJsonBody, string? body, bool bodyEmpty = false)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = new PathString("/pet");

        if (hasJsonBody && body is not null)
        {
            context.Items["SpecGuard.ParsedBody"] = JsonDocument.Parse(body).RootElement;
        }
        else if (hasJsonBody && bodyEmpty)
        {
            context.Items["SpecGuard.BodyEmpty"] = true;
        }

        return context;
    }
}
