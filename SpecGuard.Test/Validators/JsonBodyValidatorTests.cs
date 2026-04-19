using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SpecGuard.Test.Validators;

public class JsonBodyValidatorTests
{
    private readonly JsonBodyValidator validator = new();

    [Fact]
    public async Task ValidJsonBody_returns_success()
    {
        var context = BuildContext("""{"name":"Fido"}""");

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task NonJsonContentType_skips_parsing_and_returns_success()
    {
        // No items set = middleware didn't parse (non-JSON content type)
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = new PathString("/");

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task MissingContentType_skips_parsing_and_returns_success()
    {
        // No items set = middleware didn't parse (no content type)
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = new PathString("/");

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Request_body_missing_required_field_returns_errors()
    {
        var validator = new JsonBodyValidator();
        validator.Initialize(PetSpec());

        var context = BuildContext("""{"age":3}""", path: "/pet");

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Request_body_conforming_to_schema_returns_success()
    {
        var validator = new JsonBodyValidator();
        validator.Initialize(PetSpec());

        var context = BuildContext("""{"name":"Fido"}""", path: "/pet");

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Request_with_no_matching_operation_skips_schema_validation()
    {
        var validator = new JsonBodyValidator();
        validator.Initialize(PetSpec());

        var context = BuildContext("""{}""", path: "/unknown");

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Error_path_reports_instance_location_for_nested_field()
    {
        const string spec = """
            {
              "paths": {
                "/widgets": {
                  "post": {
                    "requestBody": {
                      "content": {
                        "application/json": {
                          "schema": {
                            "type": "object",
                            "properties": {
                              "count": { "type": "integer" }
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;
        var validator = new JsonBodyValidator();
        using var document = JsonDocument.Parse(spec);
        validator.Initialize(document);

        var context = BuildContext("""{"count":"not-an-int"}""", path: "/widgets");

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.NotEmpty(result);
        Assert.Contains(result, e => e.Path == "/count");
    }

    private static JsonDocument PetSpec() => JsonDocument.Parse("""
        {
          "paths": {
            "/pet": {
              "post": {
                "requestBody": {
                  "content": {
                    "application/json": {
                      "schema": { "$ref": "#/components/schemas/Pet" }
                    }
                  }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "Pet": {
                "type": "object",
                "required": ["name"],
                "properties": {
                  "name": { "type": "string" }
                }
              }
            }
          }
        }
        """);

    private static DefaultHttpContext BuildContext(string body, string path = "/")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = new PathString(path);
        context.Items["SpecGuard.ParsedBody"] = JsonDocument.Parse(body).RootElement;
        return context;
    }
}
