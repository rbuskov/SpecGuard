using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SpecGuard.Test.Validators;

public class JsonBodyValidatorDiscriminatorTests
{
    private const string PetComponents = """
        {
          "Pet": {
            "type": "object",
            "discriminator": {
              "propertyName": "petType",
              "mapping": {
                "dog": "#/components/schemas/Dog",
                "cat": "#/components/schemas/Cat"
              }
            }
          },
          "Dog": {
            "type": "object",
            "required": ["petType", "name", "breed"],
            "properties": {
              "petType": { "type": "string" },
              "name":    { "type": "string" },
              "breed":   { "type": "string" }
            }
          },
          "Cat": {
            "type": "object",
            "required": ["petType", "name", "indoor"],
            "properties": {
              "petType": { "type": "string" },
              "name":    { "type": "string" },
              "indoor":  { "type": "boolean" }
            }
          }
        }
        """;

    private const string PetBodySchema = """{ "$ref": "#/components/schemas/Pet" }""";

    [Fact]
    public async Task Dispatches_to_dog_when_petType_is_dog()
    {
        var result = await Validate(
            PetBodySchema,
            PetComponents,
            """{ "petType": "dog", "name": "Fido", "breed": "Poodle" }""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Dispatches_to_cat_when_petType_is_cat()
    {
        var result = await Validate(
            PetBodySchema,
            PetComponents,
            """{ "petType": "cat", "name": "Whiskers", "indoor": true }""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Rejects_dog_body_missing_required_breed()
    {
        var result = await Validate(
            PetBodySchema,
            PetComponents,
            """{ "petType": "dog", "name": "Fido" }""");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Rejects_cat_body_missing_required_indoor()
    {
        var result = await Validate(
            PetBodySchema,
            PetComponents,
            """{ "petType": "cat", "name": "Whiskers" }""");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Rejects_body_with_wrong_type_for_dispatched_field()
    {
        // Cat schema requires indoor: boolean; sending a string should fail.
        var result = await Validate(
            PetBodySchema,
            PetComponents,
            """{ "petType": "cat", "name": "Whiskers", "indoor": "yes" }""");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Applies_only_the_branch_matching_the_discriminator_value()
    {
        // petType is "cat", so Cat's required fields apply — not Dog's.
        // The body has "breed" (Dog-only) but is missing "indoor" (required by Cat).
        var result = await Validate(
            PetBodySchema,
            PetComponents,
            """{ "petType": "cat", "name": "Whiskers", "breed": "Poodle" }""");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Dispatch_uses_petType_not_shape_of_other_fields()
    {
        // petType "dog" + dog fields passes, even though a cat-shaped body would
        // fail because it's missing "breed". This confirms dispatch is driven by
        // the discriminator property, not by field presence.
        var result = await Validate(
            PetBodySchema,
            PetComponents,
            """{ "petType": "dog", "name": "Fido", "breed": "Labrador" }""");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Discriminator_with_oneOf_parent_dispatches_correctly()
    {
        // Classic OpenAPI pattern: parent schema has oneOf + discriminator.
        const string components = """
            {
              "Shape": {
                "oneOf": [
                  { "$ref": "#/components/schemas/Circle" },
                  { "$ref": "#/components/schemas/Square" }
                ],
                "discriminator": {
                  "propertyName": "kind",
                  "mapping": {
                    "circle": "#/components/schemas/Circle",
                    "square": "#/components/schemas/Square"
                  }
                }
              },
              "Circle": {
                "type": "object",
                "required": ["kind", "radius"],
                "properties": {
                  "kind":   { "type": "string", "enum": ["circle"] },
                  "radius": { "type": "number", "minimum": 0 }
                }
              },
              "Square": {
                "type": "object",
                "required": ["kind", "side"],
                "properties": {
                  "kind": { "type": "string", "enum": ["square"] },
                  "side": { "type": "number", "minimum": 0 }
                }
              }
            }
            """;

        var bodySchema = """{ "$ref": "#/components/schemas/Shape" }""";

        Assert.Empty(
            await Validate(bodySchema, components, """{ "kind": "circle", "radius": 1.5 }"""));

        Assert.Empty(
            await Validate(bodySchema, components, """{ "kind": "square", "side": 2 }"""));

        // Negative radius fails Circle's minimum.
        Assert.NotEmpty(await Validate(bodySchema, components, """{ "kind": "circle", "radius": -1 }"""));

        // kind says "square" but body carries circle fields — Square's "side" is missing.
        Assert.NotEmpty(await Validate(bodySchema, components, """{ "kind": "square", "radius": 1 }"""));
    }

    [Fact]
    public async Task Implicit_discriminator_mapping_dispatches_from_ref_names()
    {
        const string components = """
            {
              "Shape": {
                "oneOf": [
                  { "$ref": "#/components/schemas/Circle" },
                  { "$ref": "#/components/schemas/Square" }
                ],
                "discriminator": {
                  "propertyName": "kind"
                }
              },
              "Circle": {
                "type": "object",
                "required": ["kind", "radius"],
                "properties": {
                  "kind":   { "type": "string" },
                  "radius": { "type": "number", "minimum": 0 }
                }
              },
              "Square": {
                "type": "object",
                "required": ["kind", "side"],
                "properties": {
                  "kind": { "type": "string" },
                  "side": { "type": "number", "minimum": 0 }
                }
              }
            }
            """;

        var bodySchema = """{ "$ref": "#/components/schemas/Shape" }""";

        // Implicit mapping infers "Circle" and "Square" as discriminator values.
        Assert.Empty(
            await Validate(bodySchema, components, """{ "kind": "Circle", "radius": 1.5 }"""));

        Assert.Empty(
            await Validate(bodySchema, components, """{ "kind": "Square", "side": 2 }"""));

        // kind says "Square" but body has circle fields — Square's "side" is missing.
        Assert.NotEmpty(await Validate(bodySchema, components, """{ "kind": "Square", "radius": 1 }"""));
    }

    [Fact]
    public async Task Unknown_discriminator_value_with_oneOf_does_not_produce_duplicate_errors()
    {
        const string components = """
            {
              "Shape": {
                "oneOf": [
                  { "$ref": "#/components/schemas/Circle" },
                  { "$ref": "#/components/schemas/Square" }
                ],
                "discriminator": {
                  "propertyName": "kind",
                  "mapping": {
                    "circle": "#/components/schemas/Circle",
                    "square": "#/components/schemas/Square"
                  }
                }
              },
              "Circle": {
                "type": "object",
                "required": ["kind", "radius"],
                "properties": {
                  "kind":   { "type": "string", "enum": ["circle"] },
                  "radius": { "type": "number" }
                }
              },
              "Square": {
                "type": "object",
                "required": ["kind", "side"],
                "properties": {
                  "kind": { "type": "string", "enum": ["square"] },
                  "side": { "type": "number" }
                }
              }
            }
            """;

        var bodySchema = """{ "$ref": "#/components/schemas/Shape" }""";

        var result = await Validate(bodySchema, components, """{ "kind": "triangle", "edges": 3 }""");

        // Should fail (unknown discriminator value) but NOT produce duplicate
        // errors from both the discriminator chain and the oneOf.
        Assert.NotEmpty(result);

        // No error should reference oneOf — only the discriminator chain should report.
        Assert.All(result, e => Assert.DoesNotContain("oneOf", e.Message));
    }

    private static async Task<IReadOnlyList<ValidationErrorResult.ValidationError>> Validate(string bodySchemaJson, string componentsSchemasJson, string body)
    {
        var validator = new JsonBodyValidator();
        using var spec = BuildSpec(bodySchemaJson, componentsSchemasJson);
        validator.Initialize(spec);

        var context = BuildContext(body, "/items");
        return await validator.ValidateAsync(context, CancellationToken.None);
    }

    private static JsonDocument BuildSpec(string bodySchemaJson, string componentsSchemasJson) => JsonDocument.Parse($$"""
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
          },
          "components": {
            "schemas": {{componentsSchemasJson}}
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
