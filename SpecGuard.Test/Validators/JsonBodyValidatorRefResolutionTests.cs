using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SpecGuard.Test.Validators;

public class JsonBodyValidatorRefResolutionTests
{
    [Fact]
    public async Task Component_ref_resolves_and_validates()
    {
        const string components = """
            {
              "Pet": {
                "type": "object",
                "required": ["name"],
                "properties": { "name": { "type": "string" } }
              }
            }
            """;

        var bodySchema = """{ "$ref": "#/components/schemas/Pet" }""";

        Assert.Empty(
            await Validate(bodySchema, components, """{ "name": "Fido" }"""));

        Assert.NotEmpty(await Validate(bodySchema, components, """{ "age": 3 }"""));
    }

    [Fact]
    public async Task Nested_ref_chain_resolves()
    {
        // A -> B -> C
        const string components = """
            {
              "A": { "$ref": "#/components/schemas/B" },
              "B": { "$ref": "#/components/schemas/C" },
              "C": {
                "type": "object",
                "required": ["id"],
                "properties": { "id": { "type": "integer" } }
              }
            }
            """;

        var bodySchema = """{ "$ref": "#/components/schemas/A" }""";

        Assert.Empty(
            await Validate(bodySchema, components, """{ "id": 1 }"""));

        Assert.NotEmpty(await Validate(bodySchema, components, """{ "id": "not-int" }"""));
        Assert.NotEmpty(await Validate(bodySchema, components, """{}"""));
    }

    [Fact]
    public async Task Ref_inside_array_items_resolves()
    {
        const string components = """
            {
              "Tag": {
                "type": "object",
                "required": ["label"],
                "properties": { "label": { "type": "string" } }
              }
            }
            """;

        var bodySchema = """
            {
              "type": "array",
              "items": { "$ref": "#/components/schemas/Tag" }
            }
            """;

        Assert.Empty(
            await Validate(bodySchema, components, """[{ "label": "one" }, { "label": "two" }]"""));

        Assert.NotEmpty(await Validate(bodySchema, components, """[{ "label": "one" }, {}]"""));
    }

    [Fact]
    public async Task Ref_inside_object_property_resolves()
    {
        const string components = """
            {
              "Address": {
                "type": "object",
                "required": ["city"],
                "properties": { "city": { "type": "string" } }
              }
            }
            """;

        var bodySchema = """
            {
              "type": "object",
              "required": ["address"],
              "properties": {
                "address": { "$ref": "#/components/schemas/Address" }
              }
            }
            """;

        Assert.Empty(
            await Validate(bodySchema, components, """{ "address": { "city": "Oslo" } }"""));

        Assert.NotEmpty(await Validate(bodySchema, components, """{ "address": {} }"""));
    }

    [Fact]
    public async Task Same_ref_used_from_multiple_locations()
    {
        const string components = """
            {
              "Name": {
                "type": "string",
                "minLength": 1
              }
            }
            """;

        var bodySchema = """
            {
              "type": "object",
              "properties": {
                "first": { "$ref": "#/components/schemas/Name" },
                "last":  { "$ref": "#/components/schemas/Name" }
              }
            }
            """;

        Assert.Empty(
            await Validate(bodySchema, components, """{ "first": "Ada", "last": "Lovelace" }"""));

        Assert.NotEmpty(await Validate(bodySchema, components, """{ "first": "", "last": "Lovelace" }"""));
    }

    [Fact]
    public async Task Circular_ref_validates_finite_tree()
    {
        // Tree self-references via its "children" array.
        const string components = """
            {
              "TreeNode": {
                "type": "object",
                "required": ["value"],
                "properties": {
                  "value": { "type": "string" },
                  "children": {
                    "type": "array",
                    "items": { "$ref": "#/components/schemas/TreeNode" }
                  }
                }
              }
            }
            """;

        var bodySchema = """{ "$ref": "#/components/schemas/TreeNode" }""";

        const string validTree = """
            {
              "value": "root",
              "children": [
                {
                  "value": "a",
                  "children": [
                    { "value": "a1" }
                  ]
                },
                { "value": "b" }
              ]
            }
            """;

        Assert.Empty(
            await Validate(bodySchema, components, validTree));
    }

    [Fact]
    public async Task Circular_ref_detects_invalid_field_at_deep_level()
    {
        const string components = """
            {
              "TreeNode": {
                "type": "object",
                "required": ["value"],
                "properties": {
                  "value": { "type": "string" },
                  "children": {
                    "type": "array",
                    "items": { "$ref": "#/components/schemas/TreeNode" }
                  }
                }
              }
            }
            """;

        var bodySchema = """{ "$ref": "#/components/schemas/TreeNode" }""";

        // Deep leaf has value:42 instead of a string.
        const string invalidTree = """
            {
              "value": "root",
              "children": [
                {
                  "value": "a",
                  "children": [
                    { "value": 42 }
                  ]
                }
              ]
            }
            """;

        var result = await Validate(bodySchema, components, invalidTree);

        Assert.NotEmpty(result);
        // The error path should point at the deeply-nested /value field.
        Assert.Contains(result, e => e.Path == "/children/0/children/0/value");
    }

    [Fact]
    public async Task Circular_ref_detects_missing_required_at_nested_level()
    {
        const string components = """
            {
              "TreeNode": {
                "type": "object",
                "required": ["value"],
                "properties": {
                  "value": { "type": "string" },
                  "children": {
                    "type": "array",
                    "items": { "$ref": "#/components/schemas/TreeNode" }
                  }
                }
              }
            }
            """;

        var bodySchema = """{ "$ref": "#/components/schemas/TreeNode" }""";

        const string invalidTree = """
            {
              "value": "root",
              "children": [
                {
                  "children": [
                    { "value": "leaf" }
                  ]
                }
              ]
            }
            """;

        Assert.NotEmpty(await Validate(bodySchema, components, invalidTree));
    }

    [Fact]
    public async Task Mutually_recursive_refs_resolve()
    {
        // Category has subcategories; Subcategory refers back to Category.
        const string components = """
            {
              "Category": {
                "type": "object",
                "required": ["name"],
                "properties": {
                  "name": { "type": "string" },
                  "sub":  { "$ref": "#/components/schemas/Subcategory" }
                }
              },
              "Subcategory": {
                "type": "object",
                "required": ["slug"],
                "properties": {
                  "slug":  { "type": "string" },
                  "parent": { "$ref": "#/components/schemas/Category" }
                }
              }
            }
            """;

        var bodySchema = """{ "$ref": "#/components/schemas/Category" }""";

        const string valid = """
            {
              "name": "animals",
              "sub": {
                "slug": "dogs",
                "parent": { "name": "pets" }
              }
            }
            """;

        Assert.Empty(
            await Validate(bodySchema, components, valid));

        const string invalid = """
            {
              "name": "animals",
              "sub": { "parent": { "name": "pets" } }
            }
            """;

        Assert.NotEmpty(await Validate(bodySchema, components, invalid));
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
