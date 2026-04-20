using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpecGuard.Test.Schemas;

public class OpenApiSchemaBuilderTests
{
    [Fact]
    public void Adds_schema_and_defs_at_root()
    {
        var operationSchema = Parse("""{ "type": "object" }""");
        var components = Parse("""{ "Pet": { "type": "object" } }""");

        var result = OpenApiSchemaBuilder.Build(operationSchema, components);

        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            result["$schema"]!.GetValue<string>());
        Assert.IsType<JsonObject>(result["$defs"]);
        Assert.NotNull(result["$defs"]!["Pet"]);
    }

    [Fact]
    public void Rewrites_component_refs_to_defs()
    {
        var operationSchema = Parse("""{ "$ref": "#/components/schemas/Pet" }""");
        var components = Parse("""
            {
              "Pet": {
                "type": "object",
                "properties": { "friend": { "$ref": "#/components/schemas/Pet" } }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(operationSchema, components);

        Assert.Equal("#/$defs/Pet", result["$ref"]!.GetValue<string>());
        var friendRef = result["$defs"]!["Pet"]!["properties"]!["friend"]!["$ref"]!;
        Assert.Equal("#/$defs/Pet", friendRef.GetValue<string>());
    }

    [Fact]
    public void Strips_openapi_only_keywords()
    {
        var operationSchema = Parse("""
            {
              "type": "object",
              "deprecated": true,
              "externalDocs": { "url": "https://example.com" },
              "xml": { "name": "pet" },
              "example": { "name": "Fido" },
              "properties": {
                "name": { "type": "string", "example": "Fido", "xml": { "name": "n" } }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(operationSchema, componentsSchemas: null);

        Assert.Null(result["deprecated"]);
        Assert.Null(result["externalDocs"]);
        Assert.Null(result["xml"]);
        Assert.Null(result["example"]);
        var nameProp = result["properties"]!["name"]!.AsObject();
        Assert.Null(nameProp["example"]);
        Assert.Null(nameProp["xml"]);
        Assert.Equal("string", nameProp["type"]!.GetValue<string>());
    }

    [Fact]
    public void Empty_defs_when_no_components()
    {
        var operationSchema = Parse("""{ "type": "object" }""");

        var result = OpenApiSchemaBuilder.Build(operationSchema, componentsSchemas: null);

        Assert.Empty(result["$defs"]!.AsObject());
    }

    [Fact]
    public void Converts_discriminator_mapping_to_if_then_else_chain()
    {
        var operationSchema = Parse("""
            {
              "type": "object",
              "discriminator": {
                "propertyName": "petType",
                "mapping": {
                  "cat": "#/components/schemas/Cat",
                  "dog": "#/components/schemas/Dog"
                }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(operationSchema, componentsSchemas: null);

        Assert.Null(result["discriminator"]);
        var allOf = Assert.IsType<JsonArray>(result["allOf"]);
        var chain = Assert.IsType<JsonObject>(allOf[0]);

        var firstIf = chain["if"]!["properties"]!["petType"]!["const"]!.GetValue<string>();
        Assert.Equal("cat", firstIf);
        Assert.Equal("#/$defs/Cat", chain["then"]!["$ref"]!.GetValue<string>());

        var elseBranch = Assert.IsType<JsonObject>(chain["else"]);
        var secondIf = elseBranch["if"]!["properties"]!["petType"]!["const"]!.GetValue<string>();
        Assert.Equal("dog", secondIf);
        Assert.Equal("#/$defs/Dog", elseBranch["then"]!["$ref"]!.GetValue<string>());
    }

    [Fact]
    public void Infers_discriminator_mapping_from_oneOf_refs()
    {
        var operationSchema = Parse("""
            {
              "oneOf": [
                { "$ref": "#/components/schemas/Cat" },
                { "$ref": "#/components/schemas/Dog" }
              ],
              "discriminator": {
                "propertyName": "petType"
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(operationSchema, componentsSchemas: null);

        Assert.Null(result["discriminator"]);
        var allOf = Assert.IsType<JsonArray>(result["allOf"]);
        var chain = Assert.IsType<JsonObject>(allOf[0]);

        var firstIf = chain["if"]!["properties"]!["petType"]!["const"]!.GetValue<string>();
        Assert.Equal("Cat", firstIf);
        Assert.Equal("#/$defs/Cat", chain["then"]!["$ref"]!.GetValue<string>());

        var elseBranch = Assert.IsType<JsonObject>(chain["else"]);
        var secondIf = elseBranch["if"]!["properties"]!["petType"]!["const"]!.GetValue<string>();
        Assert.Equal("Dog", secondIf);
        Assert.Equal("#/$defs/Dog", elseBranch["then"]!["$ref"]!.GetValue<string>());
    }

    [Fact]
    public void Non_object_operation_schema_is_wrapped_in_allOf()
    {
        var operationSchema = Parse("true");

        var result = OpenApiSchemaBuilder.Build(operationSchema, componentsSchemas: null);

        Assert.NotNull(result["$schema"]);
        var allOf = Assert.IsType<JsonArray>(result["allOf"]);
        Assert.Single(allOf);
    }

    [Fact]
    public void Strips_openapi_specific_format_values()
    {
        var operationSchema = Parse("""
            {
              "type": "object",
              "properties": {
                "count": { "type": "integer", "format": "int32" },
                "big": { "type": "integer", "format": "int64" },
                "price": { "type": "number", "format": "float" },
                "precise": { "type": "number", "format": "double" },
                "file": { "type": "string", "format": "binary" },
                "encoded": { "type": "string", "format": "byte" },
                "secret": { "type": "string", "format": "password" }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(operationSchema, componentsSchemas: null);
        var props = result["properties"]!.AsObject();

        foreach (var prop in props)
        {
            Assert.Null(prop.Value!["format"]);
        }
    }

    [Fact]
    public void Preserves_standard_json_schema_format_values()
    {
        var operationSchema = Parse("""
            {
              "type": "object",
              "properties": {
                "created": { "type": "string", "format": "date-time" },
                "contact": { "type": "string", "format": "email" },
                "id": { "type": "string", "format": "uuid" },
                "addr": { "type": "string", "format": "ipv4" }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(operationSchema, componentsSchemas: null);
        var props = result["properties"]!.AsObject();

        Assert.Equal("date-time", props["created"]!["format"]!.GetValue<string>());
        Assert.Equal("email", props["contact"]!["format"]!.GetValue<string>());
        Assert.Equal("uuid", props["id"]!["format"]!.GetValue<string>());
        Assert.Equal("ipv4", props["addr"]!["format"]!.GetValue<string>());
    }

    [Fact]
    public void Rewrites_nullable_oneOf_to_anyOf()
    {
        var operationSchema = Parse("""
            {
              "type": "object",
              "properties": {
                "status": {
                  "oneOf": [
                    { "type": "null" },
                    { "$ref": "#/components/schemas/Status" }
                  ]
                }
              }
            }
            """);
        var components = Parse("""
            {
              "Status": { "enum": ["active", "inactive", null] }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(operationSchema, components);

        var statusProp = result["properties"]!["status"]!.AsObject();
        Assert.Null(statusProp["oneOf"]);
        var anyOf = Assert.IsType<JsonArray>(statusProp["anyOf"]);
        Assert.Equal(2, anyOf.Count);

        // Component enum retains its null value — nullability is handled by anyOf.
        var enumArray = result["$defs"]!["Status"]!["enum"]!.AsArray();
        Assert.Equal(3, enumArray.Count);
    }

    [Fact]
    public void Rewrites_nullable_oneOf_to_anyOf_when_null_branch_has_description()
    {
        var operationSchema = Parse("""
            {
              "type": "object",
              "properties": {
                "status": {
                  "oneOf": [
                    { "type": "null", "description": "No value provided" },
                    { "$ref": "#/components/schemas/Status" }
                  ]
                }
              }
            }
            """);
        var components = Parse("""
            {
              "Status": { "enum": ["active", "inactive", null] }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(operationSchema, components);

        var statusProp = result["properties"]!["status"]!.AsObject();
        Assert.Null(statusProp["oneOf"]);
        var anyOf = Assert.IsType<JsonArray>(statusProp["anyOf"]);
        Assert.Equal(2, anyOf.Count);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
