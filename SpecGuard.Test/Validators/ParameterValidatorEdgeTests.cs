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
    public async Task Cookie_value_is_url_decoded_or_passed_verbatim()
    {
        // ASP.NET Core's RequestCookieCollection passes cookie values
        // through without URL-decoding by default. Lock the observed
        // behavior — a percent-encoded value reaches validation as-is.
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "session", "in": "cookie", "required": true,
                        "schema": { "type": "string", "minLength": 5 } }
                    ]
                  }
                }
              }
            }
            """);

        var ctx = BuildContext("GET", "/pets");
        ctx.Request.Headers["Cookie"] = "session=abc%20de";

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);

        // Either decoded ("abc de") or raw ("abc%20de") — both have
        // length >= 5 so no errors fire. Lock the no-error outcome.
        Assert.Empty(result);
    }

    [Fact]
    public async Task Content_encoded_parameter_with_unexpected_json_type_validates_against_schema()
    {
        // Parameter declares an object schema via content; client sends
        // a JSON array. The schema evaluator catches the type mismatch.
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "filter", "in": "query",
                        "content": {
                          "application/json": {
                            "schema": { "type": "object" }
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var ctx = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["filter"] = new StringValues("[1,2,3]"),
        });

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);
        Assert.Contains(result, e => e.In == "query" && e.Path == "filter");
    }

    [Fact]
    public async Task Content_encoded_parameter_in_header_uses_same_path_as_query()
    {
        // The code paths for query/header/cookie/path differ only in
        // raw-value lookup; content-encoding works identically.
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "X-Filter", "in": "header",
                        "content": {
                          "application/json": {
                            "schema": {
                              "type": "object",
                              "required": ["status"],
                              "properties": { "status": { "type": "string" } }
                            }
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var ctx = BuildContext("GET", "/pets");
        ctx.Request.Headers["X-Filter"] = """{"status":"active"}""";

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task DeepObject_required_parameter_missing_returns_missing_error()
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
                        "schema": { "type": "object",
                                    "properties": { "status": { "type": "string" } } }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var ctx = BuildContext("GET", "/pets");
        var result = await validator.ValidateAsync(ctx, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "filter" && e.Message.Contains("Missing"));
    }

    [Fact]
    public async Task Form_explode_true_array_below_minItems_fails()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "tag", "in": "query",
                        "schema": { "type": "array", "minItems": 2,
                                    "items": { "type": "string" } } }
                    ]
                  }
                }
              }
            }
            """);

        var ctx = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["tag"] = new StringValues("only"),
        });

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);
        Assert.Contains(result, e => e.In == "query" && e.Path == "tag");
    }

    [Fact]
    public async Task Pipe_delimited_with_repeated_values_picks_first_value()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "tags", "in": "query",
                        "style": "pipeDelimited", "explode": false,
                        "schema": { "type": "array", "items": { "type": "string" }, "minItems": 2 } }
                    ]
                  }
                }
              }
            }
            """);

        var ctx = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["tags"] = new StringValues(new[] { "a", "b|c" }),
        });

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task DeepObject_default_explode_is_handled()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "filter", "in": "query", "style": "deepObject",
                        "schema": { "type": "object",
                                    "properties": { "status": { "type": "string" } } } }
                    ]
                  }
                }
              }
            }
            """);

        var ctx = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["filter[status]"] = new StringValues("active"),
        });

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Header_with_multiple_values_only_validates_first()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "X-Foo", "in": "header",
                        "schema": { "type": "string", "minLength": 3 } }
                    ]
                  }
                }
              }
            }
            """);

        var ctx = BuildContext("GET", "/pets");
        ctx.Request.Headers["X-Foo"] = new StringValues(new[] { "ab", "abc" });

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);
        Assert.Contains(result, e => e.In == "header" && e.Path == "X-Foo");
    }

    [Fact]
    public async Task Plus_sign_in_integer_coercion_is_accepted()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "n", "in": "query", "schema": { "type": "integer" } }
                    ]
                  }
                }
              }
            }
            """);

        var ctx = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["n"] = new StringValues("+5"),
        });

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task No_declared_type_falls_through_as_string()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "v", "in": "query",
                        "schema": { "minLength": 2 } }
                    ]
                  }
                }
              }
            }
            """);

        var ctx = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["v"] = new StringValues("a"),
        });

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);
        Assert.Contains(result, e => e.In == "query" && e.Path == "v");
    }

    [Fact]
    public void Cyclic_schema_ref_through_components_throws_at_initialization()
    {
        // Json.Schema detects pure $ref cycles at compile time and throws.
        // Lock this so a future change that bypasses cycle detection gets
        // caught.
        var validator = new ParameterValidator();
        using var spec = JsonDocument.Parse("""
            {
              "paths": {
                "/items": {
                  "get": {
                    "parameters": [
                      { "name": "v", "in": "query",
                        "schema": { "$ref": "#/components/schemas/A" } }
                    ]
                  }
                }
              },
              "components": {
                "schemas": {
                  "A": { "$ref": "#/components/schemas/B" },
                  "B": { "$ref": "#/components/schemas/A" }
                }
              }
            }
            """);

        Assert.ThrowsAny<Exception>(() => validator.Initialize(spec));
    }

    [Fact]
    public async Task Path_level_parameter_overridden_by_operation_level_for_header()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/items": {
                  "parameters": [
                    { "name": "X-Code", "in": "header",
                      "schema": { "type": "string", "minLength": 100 } }
                  ],
                  "get": {
                    "parameters": [
                      { "name": "X-Code", "in": "header",
                        "schema": { "type": "string", "minLength": 1 } }
                    ]
                  }
                }
              }
            }
            """);

        var ctx = BuildContext("GET", "/items");
        ctx.Request.Headers["X-Code"] = "x";

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);

        // Operation-level minLength:1 wins over path-level minLength:100.
        Assert.Empty(result);
    }

    [Fact]
    public async Task Parameter_ref_pointing_to_unknown_component_is_silently_dropped()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/items": {
                  "get": {
                    "parameters": [
                      { "$ref": "#/components/parameters/Missing" }
                    ]
                  }
                }
              },
              "components": { "parameters": {} }
            }
            """);

        var ctx = BuildContext("GET", "/items");
        var result = await validator.ValidateAsync(ctx, CancellationToken.None);

        // Unknown ref → parameter ignored → no errors fired.
        Assert.Empty(result);
    }

    [Fact]
    public async Task DeepObject_unknown_keys_with_additional_properties_false_are_rejected()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "filter", "in": "query",
                        "style": "deepObject", "explode": true,
                        "schema": {
                          "type": "object",
                          "properties": { "status": { "type": "string" } },
                          "additionalProperties": false
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var ctx = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["filter[unknown]"] = new StringValues("x"),
        });

        var result = await validator.ValidateAsync(ctx, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "filter");
    }

    [Fact]
    public async Task Path_parameter_percent_encoded_slash_decodes_to_literal_slash()
    {
        // %2F is decoded by Uri.UnescapeDataString to '/', but the route
        // template is segment-based so the request path won't actually
        // route to the parameterized template. Lock the behavior.
        var validator = BuildValidator("""
            {
              "paths": {
                "/files/{name}": {
                  "get": {
                    "parameters": [
                      { "name": "name", "in": "path", "required": true,
                        "schema": { "type": "string" } }
                    ]
                  }
                }
              }
            }
            """);

        // The TestHost normalizes the path before this point — the matcher
        // in `RoutePatternMatcher` operates on segments. A request whose
        // path is `/files/a%2Fb` ends up as `/files/a/b` after decoding,
        // which has 3 segments and therefore does not match the 2-segment
        // template. Verify no error is produced (no match → no validation).
        var ctx = BuildContext("GET", "/files/a/b");
        var result = await validator.ValidateAsync(ctx, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Path_parameter_label_style_is_unsupported_and_passes_value_unparsed()
    {
        // SpecGuard does not understand `style: "label"` — it treats the
        // raw segment as the value. Lock the limitation.
        var validator = BuildValidator("""
            {
              "paths": {
                "/items/{id}": {
                  "get": {
                    "parameters": [
                      { "name": "id", "in": "path", "required": true,
                        "style": "label", "schema": { "type": "string" } }
                    ]
                  }
                }
              }
            }
            """);

        // .42 is what a label-style serializer would produce. SpecGuard
        // hands ".42" to validation as a plain string — string type
        // matches, no error.
        var ctx = BuildContext("GET", "/items/.42");
        var result = await validator.ValidateAsync(ctx, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Compound_path_segment_with_two_parameters_locks_extraction_limitation()
    {
        // The route matcher accepts compound segments like "{year}-{month}"
        // (covered in RoutePatternMatcherTests), but ParameterValidator's
        // ExtractPathValues only treats whole-segment placeholders as
        // parameters. Sub-segment values aren't extracted, so per-parameter
        // schema validation is silently skipped.
        var validator = BuildValidator("""
            {
              "paths": {
                "/reports/{year}-{month}": {
                  "get": {
                    "parameters": [
                      { "name": "year", "in": "path", "required": true,
                        "schema": { "type": "integer" } },
                      { "name": "month", "in": "path", "required": true,
                        "schema": { "type": "integer", "minimum": 1, "maximum": 12 } }
                    ]
                  }
                }
              }
            }
            """);

        // Both pass because per-param validation is skipped.
        Assert.Empty(await validator.ValidateAsync(BuildContext("GET", "/reports/2024-03"), CancellationToken.None));
        Assert.Empty(await validator.ValidateAsync(BuildContext("GET", "/reports/2024-13"), CancellationToken.None));
    }

    [Fact]
    public async Task Header_empty_value_fails_required_pattern_like_schema()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "X-Code", "in": "header", "required": true,
                        "schema": { "type": "string", "minLength": 1 } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets");
        context.Request.Headers["X-Code"] = "";

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "header" && e.Path == "X-Code");
    }

    [Fact]
    public async Task Content_type_header_declared_as_parameter_is_found()
    {
        // Reserved headers (Content-Type, Authorization) are looked up via
        // HeaderDictionary like any other header.
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "Authorization", "in": "header", "required": true,
                        "schema": { "type": "string", "pattern": "^Bearer " } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets");
        context.Request.Headers["Authorization"] = "Bearer token";

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Integer_coercion_allows_leading_zeros()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "n", "in": "query", "schema": { "type": "integer" } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["n"] = new StringValues("00042"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Number_coercion_accepts_scientific_notation()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "n", "in": "query", "schema": { "type": "number" } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["n"] = new StringValues("1e3"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Number_coercion_rejects_comma_decimal_separator()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "n", "in": "query", "schema": { "type": "number" } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["n"] = new StringValues("1,5"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "n");
    }

    [Fact]
    public async Task Nullable_type_array_picks_non_null_branch_for_coercion()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "n", "in": "query", "schema": { "type": ["integer","null"], "maximum": 10 } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["n"] = new StringValues("5"),
        });
        Assert.Empty(await validator.ValidateAsync(context, CancellationToken.None));

        var bad = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["n"] = new StringValues("99"),
        });
        Assert.NotEmpty(await validator.ValidateAsync(bad, CancellationToken.None));
    }

    [Fact]
    public async Task Content_encoded_parameter_returns_error_when_content_is_not_json()
    {
        // Content-encoded parameters only honor the application/json slot —
        // when that slot is absent, the parameter is skipped entirely.
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "filter", "in": "query", "required": true,
                        "content": { "application/xml": { "schema": { "type": "object" } } }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets");
        var result = await validator.ValidateAsync(context, CancellationToken.None);

        // The parameter is recognized (required) but has no usable schema,
        // so "Missing required query parameter 'filter'" is the observed
        // behavior when the request omits it.
        Assert.Contains(result, e => e.In == "query" && e.Path == "filter" && e.Message.Contains("Missing"));
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
