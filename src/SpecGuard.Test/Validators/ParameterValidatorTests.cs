using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace SpecGuard.Test.Validators;

public class ParameterValidatorTests
{
    [Fact]
    public async Task Unknown_operation_returns_success()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets/{id}": {
                  "get": {
                    "parameters": [
                      { "name": "id", "in": "path", "required": true, "schema": { "type": "integer" } }
                    ]
                  }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/unknown"), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Path_parameter_valid_integer_returns_success()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets/{id}": {
                  "get": {
                    "parameters": [
                      { "name": "id", "in": "path", "required": true, "schema": { "type": "integer", "minimum": 1 } }
                    ]
                  }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets/5"), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Path_parameter_wrong_type_returns_schema_error()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets/{id}": {
                  "get": {
                    "parameters": [
                      { "name": "id", "in": "path", "required": true, "schema": { "type": "integer" } }
                    ]
                  }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets/not-an-int"), CancellationToken.None);

        Assert.Contains(result, e => e.In == "path" && e.Path == "id");
    }

    [Fact]
    public async Task Path_parameter_integer_with_format_produces_single_type_error()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets/{id}": {
                  "get": {
                    "parameters": [
                      { "name": "id", "in": "path", "required": true, "schema": { "type": "integer", "format": "int64" } }
                    ]
                  }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets/abc"), CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task Path_parameter_integer_with_pattern_produces_errors()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets/{id}": {
                  "get": {
                    "parameters": [
                      { "name": "id", "in": "path", "required": true, "schema": { "type": "integer", "pattern": "^-?\\d+$" } }
                    ]
                  }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets/q"), CancellationToken.None);

        Assert.NotEmpty(result);
        Assert.Contains(result, e => e.In == "path" && e.Path == "id");
    }

    [Fact]
    public async Task Path_parameter_below_minimum_returns_schema_error()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets/{id}": {
                  "get": {
                    "parameters": [
                      { "name": "id", "in": "path", "required": true, "schema": { "type": "integer", "minimum": 1 } }
                    ]
                  }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets/0"), CancellationToken.None);

        Assert.Contains(result, e => e.In == "path" && e.Path == "id");
    }

    [Fact]
    public async Task Query_parameter_missing_required_returns_schema_error()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "limit", "in": "query", "required": true, "schema": { "type": "integer" } }
                    ]
                  }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets"), CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "limit" && e.Message.Contains("Missing"));
    }

    [Fact]
    public async Task Query_parameter_optional_missing_returns_success()
    {
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

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets"), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Query_parameter_empty_string_rejected_by_default()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "search", "in": "query", "schema": { "type": "string" } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["search"] = new StringValues(""),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "search");
    }

    [Fact]
    public async Task Query_parameter_empty_string_allowed_when_allowEmptyValue_true()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "search", "in": "query", "allowEmptyValue": true, "schema": { "type": "string" } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["search"] = new StringValues(""),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Query_parameter_integer_type_mismatch_returns_schema_error()
    {
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
            ["limit"] = new StringValues("abc"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "limit");
    }

    [Fact]
    public async Task Query_parameter_string_pattern_mismatch_returns_schema_error()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "code",
                        "in": "query",
                        "schema": { "type": "string", "pattern": "^[A-Z]{3}$", "minLength": 3, "maxLength": 3 }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["code"] = new StringValues("abcd"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "code");
    }

    [Fact]
    public async Task Query_parameter_enum_rejects_unknown_value()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "status",
                        "in": "query",
                        "schema": { "type": "string", "enum": ["available", "pending", "sold"] }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["status"] = new StringValues("exploded"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "status");
    }

    [Fact]
    public async Task Query_array_explode_true_with_multiple_values_validates()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "tag",
                        "in": "query",
                        "schema": {
                          "type": "array",
                          "minItems": 2,
                          "items": { "type": "string" }
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
            ["tag"] = new StringValues(new[] { "dog", "cat" }),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Query_array_explode_false_comma_delimited_validates()
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
                          "minItems": 2
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
            ["ids"] = new StringValues("1,2,3"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Query_array_too_few_items_returns_schema_error()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "tag",
                        "in": "query",
                        "schema": {
                          "type": "array",
                          "minItems": 2,
                          "items": { "type": "string" }
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
            ["tag"] = new StringValues("dog"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "tag");
    }

    [Fact]
    public async Task Header_parameter_required_missing_returns_schema_error()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "X-Request-Id", "in": "header", "required": true, "schema": { "type": "string" } }
                    ]
                  }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets"), CancellationToken.None);

        Assert.Contains(result, e => e.In == "header" && e.Path == "X-Request-Id");
    }

    [Fact]
    public async Task Header_parameter_present_with_valid_uuid_returns_success()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "X-Request-Id",
                        "in": "header",
                        "required": true,
                        "schema": { "type": "string", "format": "uuid" }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets");
        context.Request.Headers["X-Request-Id"] = "550e8400-e29b-41d4-a716-446655440000";

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Cookie_parameter_validates()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "session", "in": "cookie", "required": true, "schema": { "type": "string", "minLength": 5 } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets");
        context.Request.Headers["Cookie"] = "session=abc";

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "cookie" && e.Path == "session");
    }

    [Fact]
    public async Task Cookie_parameter_missing_required_returns_schema_error()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "session", "in": "cookie", "required": true, "schema": { "type": "string" } }
                    ]
                  }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets"), CancellationToken.None);

        Assert.Contains(result, e => e.In == "cookie" && e.Path == "session");
    }

    [Fact]
    public async Task Path_level_parameter_is_inherited_by_operations()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets/{id}": {
                  "parameters": [
                    { "name": "id", "in": "path", "required": true, "schema": { "type": "integer" } }
                  ],
                  "get": { }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets/oops"), CancellationToken.None);

        Assert.Contains(result, e => e.In == "path" && e.Path == "id");
    }

    [Fact]
    public async Task Operation_level_parameter_overrides_path_level()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "parameters": [
                    { "name": "limit", "in": "query", "schema": { "type": "integer", "maximum": 10 } }
                  ],
                  "get": {
                    "parameters": [
                      { "name": "limit", "in": "query", "schema": { "type": "integer", "maximum": 100 } }
                    ]
                  }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["limit"] = new StringValues("50"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Parameter_ref_from_components_is_resolved()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "$ref": "#/components/parameters/LimitParam" }
                    ]
                  }
                }
              },
              "components": {
                "parameters": {
                  "LimitParam": {
                    "name": "limit",
                    "in": "query",
                    "required": true,
                    "schema": { "type": "integer", "minimum": 1, "maximum": 100 }
                  }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets"), CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "limit" && e.Message.Contains("Missing"));
    }

    [Fact]
    public async Task Schema_ref_from_components_is_resolved()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "status", "in": "query", "schema": { "$ref": "#/components/schemas/PetStatus" } }
                    ]
                  }
                }
              },
              "components": {
                "schemas": {
                  "PetStatus": { "type": "string", "enum": ["available", "pending", "sold"] }
                }
              }
            }
            """);

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["status"] = new StringValues("not-a-status"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "status");
    }

    [Fact]
    public async Task Integer_ref_path_parameter_coerces_via_resolved_type()
    {
        // Regression: a $ref'd integer schema on a path parameter was treated as
        // a string during coercion, so valid inputs failed "string but should be integer".
        var validator = BuildValidator("""
            {
              "paths": {
                "/board/{row}": {
                  "get": {
                    "parameters": [
                      { "name": "row", "in": "path", "required": true,
                        "schema": { "$ref": "#/components/schemas/coordinate" } }
                    ]
                  }
                }
              },
              "components": {
                "schemas": {
                  "coordinate": { "type": "integer", "minimum": 1, "maximum": 3 }
                }
              }
            }
            """);

        var valid = await validator.ValidateAsync(BuildContext("GET", "/board/2"), CancellationToken.None);
        Assert.Empty(valid);

        var outOfRange = await validator.ValidateAsync(BuildContext("GET", "/board/5"), CancellationToken.None);
        Assert.Contains(outOfRange, e => e.In == "path" && e.Path == "row");
    }

    [Fact]
    public async Task Array_items_ref_coerces_each_element_to_resolved_type()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "ids", "in": "query",
                        "schema": { "type": "array",
                                    "items": { "$ref": "#/components/schemas/PetId" } } }
                    ]
                  }
                }
              },
              "components": {
                "schemas": {
                  "PetId": { "type": "integer", "minimum": 1 }
                }
              }
            }
            """);

        var valid = await validator.ValidateAsync(
            BuildContext("GET", "/pets", query: new Dictionary<string, StringValues> { ["ids"] = new StringValues("1,2,3") }),
            CancellationToken.None);
        Assert.Empty(valid);

        var invalid = await validator.ValidateAsync(
            BuildContext("GET", "/pets", query: new Dictionary<string, StringValues> { ["ids"] = new StringValues("1,0,3") }),
            CancellationToken.None);
        Assert.Contains(invalid, e => e.In == "query" && e.Path == "ids");
    }

    [Fact]
    public async Task Object_property_ref_coerces_each_property_to_resolved_type()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "filter", "in": "query", "style": "form", "explode": false,
                        "schema": {
                          "type": "object",
                          "properties": {
                            "age": { "$ref": "#/components/schemas/Age" }
                          }
                        } }
                    ]
                  }
                }
              },
              "components": {
                "schemas": {
                  "Age": { "type": "integer", "minimum": 0 }
                }
              }
            }
            """);

        var valid = await validator.ValidateAsync(
            BuildContext("GET", "/pets", query: new Dictionary<string, StringValues> { ["filter"] = new StringValues("age,5") }),
            CancellationToken.None);
        Assert.Empty(valid);

        var invalid = await validator.ValidateAsync(
            BuildContext("GET", "/pets", query: new Dictionary<string, StringValues> { ["filter"] = new StringValues("age,-1") }),
            CancellationToken.None);
        Assert.Contains(invalid, e => e.In == "query" && e.Path == "filter");
    }

    [Fact]
    public async Task Nullable_via_type_array_accepts_absent_optional()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "category", "in": "query", "schema": { "type": ["string", "null"] } }
                    ]
                  }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets"), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Boolean_query_parameter_coerces_and_validates()
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
            ["active"] = new StringValues("true"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("False")]
    [InlineData("FALSE")]
    public async Task Boolean_query_parameter_coerces_case_insensitively(string value)
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

        Assert.Empty(result);
    }

    [Fact]
    public async Task Multiple_parameter_errors_are_all_reported()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      { "name": "limit", "in": "query", "required": true, "schema": { "type": "integer" } },
                      { "name": "offset", "in": "query", "required": true, "schema": { "type": "integer" } }
                    ]
                  }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets"), CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "limit");
        Assert.Contains(result, e => e.In == "query" && e.Path == "offset");
    }

    [Fact]
    public async Task Content_encoded_query_parameter_validates_valid_json()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "filter",
                        "in": "query",
                        "content": {
                          "application/json": {
                            "schema": {
                              "type": "object",
                              "properties": {
                                "status": { "type": "string" },
                                "limit": { "type": "integer" }
                              },
                              "required": ["status"]
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

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["filter"] = new StringValues("""{"status":"active","limit":10}"""),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Content_encoded_query_parameter_rejects_invalid_schema()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "filter",
                        "in": "query",
                        "content": {
                          "application/json": {
                            "schema": {
                              "type": "object",
                              "properties": {
                                "status": { "type": "string" },
                                "limit": { "type": "integer" }
                              },
                              "required": ["status"]
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

        // Missing required "status" property.
        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["filter"] = new StringValues("""{"limit":10}"""),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "filter");
    }

    [Fact]
    public async Task Content_encoded_query_parameter_rejects_malformed_json()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "filter",
                        "in": "query",
                        "content": {
                          "application/json": {
                            "schema": {
                              "type": "object"
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

        var context = BuildContext("GET", "/pets", query: new Dictionary<string, StringValues>
        {
            ["filter"] = new StringValues("{not valid json"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "filter" && e.Message.Contains("invalid JSON"));
    }

    [Fact]
    public async Task Content_encoded_required_parameter_missing_returns_error()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "filter",
                        "in": "query",
                        "required": true,
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

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets"), CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "filter" && e.Message.Contains("Missing"));
    }

    // ── Object: deepObject style ──────────────────────────────────────

    [Fact]
    public async Task DeepObject_query_parameter_valid_returns_success()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "filter",
                        "in": "query",
                        "style": "deepObject",
                        "explode": true,
                        "schema": {
                          "type": "object",
                          "properties": {
                            "status": { "type": "string" },
                            "limit": { "type": "integer" }
                          },
                          "required": ["status"]
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
            ["filter[status]"] = new StringValues("active"),
            ["filter[limit]"] = new StringValues("10"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DeepObject_query_parameter_missing_required_property_returns_error()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "filter",
                        "in": "query",
                        "style": "deepObject",
                        "explode": true,
                        "schema": {
                          "type": "object",
                          "properties": {
                            "status": { "type": "string" },
                            "limit": { "type": "integer" }
                          },
                          "required": ["status"]
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
            ["filter[limit]"] = new StringValues("10"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "filter");
    }

    [Fact]
    public async Task DeepObject_query_parameter_wrong_property_type_returns_error()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "filter",
                        "in": "query",
                        "style": "deepObject",
                        "explode": true,
                        "schema": {
                          "type": "object",
                          "properties": {
                            "status": { "type": "string" },
                            "limit": { "type": "integer" }
                          }
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
            ["filter[status]"] = new StringValues("active"),
            ["filter[limit]"] = new StringValues("not-a-number"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "filter");
    }

    [Fact]
    public async Task DeepObject_required_parameter_absent_returns_missing_error()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "filter",
                        "in": "query",
                        "required": true,
                        "style": "deepObject",
                        "explode": true,
                        "schema": {
                          "type": "object",
                          "properties": {
                            "status": { "type": "string" }
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets"), CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "filter" && e.Message.Contains("Missing"));
    }

    [Fact]
    public async Task DeepObject_optional_parameter_absent_returns_success()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "filter",
                        "in": "query",
                        "style": "deepObject",
                        "explode": true,
                        "schema": {
                          "type": "object",
                          "properties": {
                            "status": { "type": "string" }
                          }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        var result = await validator.ValidateAsync(BuildContext("GET", "/pets"), CancellationToken.None);

        Assert.Empty(result);
    }

    // ── Object: form style, explode false ──────────────────────────────

    [Fact]
    public async Task Object_form_explode_false_comma_separated_returns_success()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "color",
                        "in": "query",
                        "style": "form",
                        "explode": false,
                        "schema": {
                          "type": "object",
                          "properties": {
                            "R": { "type": "integer" },
                            "G": { "type": "integer" },
                            "B": { "type": "integer" }
                          }
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
            ["color"] = new StringValues("R,100,G,200,B,150"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Object_form_explode_false_wrong_property_type_returns_error()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "color",
                        "in": "query",
                        "style": "form",
                        "explode": false,
                        "schema": {
                          "type": "object",
                          "properties": {
                            "R": { "type": "integer" },
                            "G": { "type": "integer" }
                          }
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
            ["color"] = new StringValues("R,100,G,not-a-number"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "color");
    }

    [Fact]
    public async Task Object_form_explode_false_missing_required_property_returns_error()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "color",
                        "in": "query",
                        "style": "form",
                        "explode": false,
                        "schema": {
                          "type": "object",
                          "properties": {
                            "R": { "type": "integer" },
                            "G": { "type": "integer" }
                          },
                          "required": ["R", "G"]
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
            ["color"] = new StringValues("R,100"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Contains(result, e => e.In == "query" && e.Path == "color");
    }

    // ── Array: space and pipe delimiters ───────────────────────────────

    [Fact]
    public async Task Query_array_space_delimited_validates()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "tags",
                        "in": "query",
                        "style": "spaceDelimited",
                        "explode": false,
                        "schema": {
                          "type": "array",
                          "items": { "type": "string" },
                          "minItems": 2
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
            ["tags"] = new StringValues("dog cat"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Query_array_pipe_delimited_validates()
    {
        var validator = BuildValidator("""
            {
              "paths": {
                "/pets": {
                  "get": {
                    "parameters": [
                      {
                        "name": "tags",
                        "in": "query",
                        "style": "pipeDelimited",
                        "explode": false,
                        "schema": {
                          "type": "array",
                          "items": { "type": "string" },
                          "minItems": 2
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
            ["tags"] = new StringValues("dog|cat"),
        });

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Query_array_integer_items_coerced_from_exploded_values()
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
                        "schema": {
                          "type": "array",
                          "items": { "type": "integer" },
                          "minItems": 2
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
            ["ids"] = new StringValues(new[] { "1", "2", "3" }),
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
