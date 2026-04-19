using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SpecGuard.Test.Validators;

public class JsonBodyValidatorStringConstraintTests
{
    [Fact]
    public async Task MinLength_rejects_string_shorter_than_minimum()
    {
        var result = await Validate("""{ "type": "string", "minLength": 3 }""", "\"ab\"");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task MinLength_accepts_string_at_minimum()
    {
        var result = await Validate("""{ "type": "string", "minLength": 3 }""", "\"abc\"");

        Assert.Empty(result);
    }

    [Fact]
    public async Task MinLength_counts_unicode_codepoints_not_utf16_code_units()
    {
        // "🎉" is one Unicode codepoint but two UTF-16 code units.
        // Per spec, minLength counts codepoints, so two emojis == length 2.
        var result = await Validate(
            """{ "type": "string", "minLength": 2, "maxLength": 2 }""",
            "\"\uD83C\uDF89\uD83C\uDF89\"");

        Assert.Empty(result);
    }

    [Fact]
    public async Task MaxLength_rejects_string_longer_than_maximum()
    {
        var result = await Validate("""{ "type": "string", "maxLength": 3 }""", "\"abcd\"");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task MaxLength_accepts_string_at_maximum()
    {
        var result = await Validate("""{ "type": "string", "maxLength": 3 }""", "\"abc\"");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Pattern_rejects_string_not_matching_regex()
    {
        var result = await Validate(
            """{ "type": "string", "pattern": "^[a-z]+$" }""",
            "\"ABC\"");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Pattern_accepts_string_matching_regex()
    {
        var result = await Validate(
            """{ "type": "string", "pattern": "^[a-z]+$" }""",
            "\"abc\"");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Pattern_is_not_anchored_by_default()
    {
        // JSON Schema regexes are unanchored — "foo123bar" matches "[0-9]+".
        var result = await Validate(
            """{ "type": "string", "pattern": "[0-9]+" }""",
            "\"foo123bar\"");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Type_string_rejects_non_string_value()
    {
        var result = await Validate("""{ "type": "string" }""", "42");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Format_email_rejects_invalid_email()
    {
        var result = await Validate(
            """{ "type": "string", "format": "email" }""",
            "\"not-an-email\"");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Format_email_accepts_valid_email()
    {
        var result = await Validate(
            """{ "type": "string", "format": "email" }""",
            "\"user@example.com\"");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Format_uuid_rejects_invalid_uuid()
    {
        var result = await Validate(
            """{ "type": "string", "format": "uuid" }""",
            "\"not-a-uuid\"");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Format_uuid_accepts_valid_uuid()
    {
        var result = await Validate(
            """{ "type": "string", "format": "uuid" }""",
            "\"550e8400-e29b-41d4-a716-446655440000\"");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Format_date_time_rejects_invalid_date_time()
    {
        var result = await Validate(
            """{ "type": "string", "format": "date-time" }""",
            "\"yesterday\"");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Format_date_time_accepts_valid_date_time()
    {
        var result = await Validate(
            """{ "type": "string", "format": "date-time" }""",
            "\"2024-01-15T12:30:45Z\"");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Format_ipv4_rejects_invalid_address()
    {
        var result = await Validate(
            """{ "type": "string", "format": "ipv4" }""",
            "\"999.999.999.999\"");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Format_unknown_is_ignored()
    {
        var result = await Validate(
            """{ "type": "string", "format": "totally-made-up" }""",
            "\"whatever\"");

        Assert.Empty(result);
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
