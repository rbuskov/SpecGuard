using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SpecGuard.Test.Validators;

public class JsonBodyValidatorWriteDirectionTests
{
    [Fact]
    public async Task WriteOnly_property_present_with_valid_value_is_accepted()
    {
        var schema = """
            {
              "type": "object",
              "required": ["password"],
              "properties": {
                "password": { "type": "string", "writeOnly": true, "minLength": 8 }
              }
            }
            """;

        var result = await Validate(schema, """{ "password": "hunter22!" }""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task WriteOnly_property_is_validated_against_its_schema()
    {
        // writeOnly doesn't change validation — a too-short password still fails minLength.
        var schema = """
            {
              "type": "object",
              "required": ["password"],
              "properties": {
                "password": { "type": "string", "writeOnly": true, "minLength": 8 }
              }
            }
            """;

        Assert.NotEmpty(await Validate(schema, """{ "password": "short" }"""));
    }

    [Fact]
    public async Task WriteOnly_required_property_is_still_required()
    {
        var schema = """
            {
              "type": "object",
              "required": ["password"],
              "properties": {
                "password": { "type": "string", "writeOnly": true }
              }
            }
            """;

        Assert.NotEmpty(await Validate(schema, "{}"));
    }

    [Fact]
    public async Task ReadOnly_property_omitted_from_request_is_accepted()
    {
        // With readOnly on a non-required property, omitting it is obviously fine.
        var schema = """
            {
              "type": "object",
              "required": ["name"],
              "properties": {
                "id":   { "type": "integer", "readOnly": true },
                "name": { "type": "string" }
              }
            }
            """;

        var result = await Validate(schema, """{ "name": "Fido" }""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadOnly_property_present_in_request_is_rejected()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "id":   { "type": "integer", "readOnly": true },
                "name": { "type": "string" }
              }
            }
            """;

        var result = await Validate(schema, """{ "id": 42, "name": "Fido" }""");

        Assert.NotEmpty(result);
        var error = Assert.Single(result);
        Assert.Equal("/id", error.Path);
        Assert.Contains("read-only", error.Message);
    }

    [Fact]
    public async Task ReadOnly_nested_property_present_in_request_is_rejected()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string" },
                "metadata": {
                  "type": "object",
                  "properties": {
                    "createdAt": { "type": "string", "readOnly": true },
                    "tag":       { "type": "string" }
                  }
                }
              }
            }
            """;

        var result = await Validate(schema, """{ "name": "Fido", "metadata": { "createdAt": "2024-01-01", "tag": "dog" } }""");

        Assert.NotEmpty(result);
        var error = Assert.Single(result);
        Assert.Equal("/metadata/createdAt", error.Path);
        Assert.Contains("read-only", error.Message);
    }

    [Fact]
    public async Task Multiple_readOnly_properties_each_produce_an_error()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "id":        { "type": "integer", "readOnly": true },
                "createdAt": { "type": "string",  "readOnly": true },
                "name":      { "type": "string" }
              }
            }
            """;

        var result = await Validate(schema, """{ "id": 1, "createdAt": "2024-01-01", "name": "Fido" }""");

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Path == "/id");
        Assert.Contains(result, e => e.Path == "/createdAt");
    }

    [Fact]
    public async Task ReadOnly_required_property_is_not_enforced_on_requests()
    {
        // Spec strategy: ignore readOnly during request validation.
        // A common OpenAPI pattern is a resource schema with `required: [id, name]`
        // reused for both responses and requests — on requests, the client can't
        // supply `id` (the server assigns it), so `readOnly` should exempt it.
        var schema = """
            {
              "type": "object",
              "required": ["id", "name"],
              "properties": {
                "id":   { "type": "integer", "readOnly": true },
                "name": { "type": "string" }
              }
            }
            """;

        var result = await Validate(schema, """{ "name": "Fido" }""");

        Assert.Empty(result);
    }

    private static async Task<IReadOnlyList<ValidationErrorResult.ValidationError>> Validate(string bodySchemaJson, string body)
    {
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec(bodySchemaJson);
        validator.Initialize(spec);

        var context = BuildContext(body, "/items");
        return await validator.ValidateAsync(context, CancellationToken.None);
    }

    private static JsonDocument BuildSpec(string bodySchemaJson) => JsonDocument.Parse($$"""
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
