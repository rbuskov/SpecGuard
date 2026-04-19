using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SpecGuard.Test.Validators;

public class RouteSpecificityTests
{
    [Fact]
    public async Task Specific_literal_route_is_matched_over_parameterized_route()
    {
        // The parameterized route appears first in the spec, but the literal
        // route /users/me should still win because it has more literal segments.
        var spec = """
            {
              "paths": {
                "/users/{id}": {
                  "post": {
                    "requestBody": {
                      "required": true,
                      "content": {
                        "application/json": {
                          "schema": {
                            "type": "object",
                            "required": ["id"],
                            "properties": {
                              "id": { "type": "integer" }
                            }
                          }
                        }
                      }
                    }
                  }
                },
                "/users/me": {
                  "post": {
                    "requestBody": {
                      "required": true,
                      "content": {
                        "application/json": {
                          "schema": {
                            "type": "object",
                            "required": ["nickname"],
                            "properties": {
                              "nickname": { "type": "string" }
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

        // This body satisfies /users/me (has nickname) but NOT /users/{id} (missing id).
        // If the parameterized route matched, we'd get a validation error.
        var context = BuildContext("/users/me", """{"nickname":"Bob"}""");

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Parameterized_route_still_matches_non_literal_paths()
    {
        var spec = """
            {
              "paths": {
                "/users/{id}": {
                  "post": {
                    "requestBody": {
                      "required": true,
                      "content": {
                        "application/json": {
                          "schema": {
                            "type": "object",
                            "required": ["id"],
                            "properties": {
                              "id": { "type": "integer" }
                            }
                          }
                        }
                      }
                    }
                  }
                },
                "/users/me": {
                  "post": {
                    "requestBody": {
                      "required": true,
                      "content": {
                        "application/json": {
                          "schema": {
                            "type": "object",
                            "required": ["nickname"],
                            "properties": {
                              "nickname": { "type": "string" }
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

        // /users/42 should match the parameterized route and require "id"
        var context = BuildContext("/users/42", """{"nickname":"Bob"}""");

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        Assert.NotEmpty(result);
    }

    private static DefaultHttpContext BuildContext(string path, string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = new PathString(path);
        context.Items["SpecGuard.ParsedBody"] = JsonDocument.Parse(body).RootElement;
        return context;
    }
}
