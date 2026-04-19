using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace SpecGuard.Test.Validators;

public class ParameterValidatorEdgeTests
{
    [Fact]
    public async Task Path_parameter_url_encoded_value_is_decoded_before_validation()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/users/{name}": {
                  "get": {
                    "parameters": [
                      { "name": "name", "in": "path", "required": true,
                        "schema": { "type": "string", "pattern": "^[a-z ]+$" } }
                    ]
                  }
                }
              }
            }
            """);

        // "%20" should be decoded to a space and then satisfy "[a-z ]+".
        var result = await validator.ValidateAsync(BuildContext("GET", "/users/ada%20lovelace"), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Header_parameter_lookup_is_case_insensitive()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "X-Request-Id", "in": "header", "required": true,
                        "schema": { "type": "string", "format": "uuid" } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets");
        // HTTP headers are case-insensitive; HeaderDictionary reflects that.
        context.Request.Headers["x-request-id"] = "550e8400-e29b-41d4-a716-446655440000";

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Query_repeated_scalar_parameter_validates_only_first_value()
    {
        // ?limit=abc&limit=10 — current behavior: first value wins. Lock it.
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "limit", "in": "query", "schema": { "type": "integer" } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["limit"] = new StringValues(new[] { "abc", "10" }),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "limit");
    }

    [Fact]
    public async Task Query_array_form_explode_false_with_single_value_produces_single_element()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "ids",
                        "in": "query",
                        "style": "form",
                        "explode": false,
                        "schema": {
                          "type": "array",
                          "items": { "type": "integer" },
                          "minItems": 1
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["ids"] = new StringValues("42"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("on")]
    public async Task Boolean_query_parameter_rejects_non_true_false_values(string value)
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "active", "in": "query", "schema": { "type": "boolean" } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["active"] = new StringValues(value),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Cookie_parameter_extracts_named_cookie_when_multiple_cookies_present()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "session", "in": "cookie", "required": true,
                        "schema": { "type": "string", "minLength": 3 } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets");
        context.Request.Headers["Cookie"] = "theme=dark; session=abcdef; lang=en";

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Cookie_parameter_missing_named_cookie_with_other_cookies_present_returns_missing()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "session", "in": "cookie", "required": true,
                        "schema": { "type": "string" } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets");
        context.Request.Headers["Cookie"] = "theme=dark; lang=en";

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "cookie" && e.Path == "session" && e.Message.Contains("Missing"));
    }

    [Fact]
    public async Task DeepObject_malformed_brackets_are_ignored_and_treated_as_absent()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "filter", "in": "query", "required": true,
                        "style": "deepObject", "explode": true,
                        "schema": { "type": "object", "properties": { "status": { "type": "string" } } }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            // Missing closing bracket: should not match the "filter[" prefix rule.
            ["filter[status"] = new StringValues("active"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "filter" && e.Message.Contains("Missing"));
    }

    [Fact]
    public async Task Integer_coercion_rejects_thousand_separator()
    {
        // NumberStyles.Integer does not include AllowThousands, so "1,000"
        // fails coercion and falls through to schema validation as a string
        // value against `type: integer`.
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "limit", "in": "query", "schema": { "type": "integer" } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["limit"] = new StringValues("1,000"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "limit");
    }

    [Fact]
    public async Task Parameter_without_schema_or_content_is_skipped_without_crashing()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "anything", "in": "query" }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["anything"] = new StringValues("42"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    private static ParameterValidator BuildValidator(string spec)
    {
        var validator = new ParameterValidator();
        validator.Initialize(JsonDocument.Parse(spec));
        return validator;
    }

    private static DefaultHttpContext BuildContext(
        string method,
        string path,
        IDictionary<string, StringValues>? query = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = new PathString(path);
        if (query is not null)
        {
            context.Request.Query = new QueryCollection(new Dictionary<string, StringValues>(query));
        }
        return context;
    }
}
