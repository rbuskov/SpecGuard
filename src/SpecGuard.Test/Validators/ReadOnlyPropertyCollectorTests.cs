using System.Text.Json;

namespace SpecGuard.Test.Validators;

public class ReadOnlyPropertyCollectorTests
{
    [Fact]
    public void Collects_top_level_readOnly_property()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": {
                "id":   { "type": "integer", "readOnly": true },
                "name": { "type": "string" }
              }
            }
            """);

        var paths = ReadOnlyPropertyCollector.Collect(schema, componentsSchemas: null);

        Assert.Contains("/id", paths);
        Assert.DoesNotContain("/name", paths);
    }

    [Fact]
    public void Collects_nested_readOnly_property_with_full_pointer_path()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": {
                "metadata": {
                  "type": "object",
                  "properties": {
                    "createdAt": { "type": "string", "readOnly": true }
                  }
                }
              }
            }
            """);

        var paths = ReadOnlyPropertyCollector.Collect(schema, componentsSchemas: null);

        Assert.Contains("/metadata/createdAt", paths);
    }

    [Fact]
    public void Collects_through_allOf_branches()
    {
        var schema = Parse("""
            {
              "allOf": [
                {
                  "type": "object",
                  "properties": { "id": { "type": "integer", "readOnly": true } }
                },
                {
                  "type": "object",
                  "properties": { "createdAt": { "type": "string", "readOnly": true } }
                }
              ]
            }
            """);

        var paths = ReadOnlyPropertyCollector.Collect(schema, componentsSchemas: null);

        Assert.Contains("/id", paths);
        Assert.Contains("/createdAt", paths);
    }

    [Fact]
    public void Collects_through_oneOf_branches()
    {
        var schema = Parse("""
            {
              "oneOf": [
                { "type": "object", "properties": { "id": { "type": "integer", "readOnly": true } } },
                { "type": "object", "properties": { "other": { "type": "string" } } }
              ]
            }
            """);

        var paths = ReadOnlyPropertyCollector.Collect(schema, componentsSchemas: null);

        Assert.Contains("/id", paths);
    }

    [Fact]
    public void Collects_through_anyOf_branches()
    {
        var schema = Parse("""
            {
              "anyOf": [
                { "type": "object", "properties": { "id": { "type": "integer", "readOnly": true } } }
              ]
            }
            """);

        var paths = ReadOnlyPropertyCollector.Collect(schema, componentsSchemas: null);

        Assert.Contains("/id", paths);
    }

    [Fact]
    public void Collects_through_ref_to_components_schema()
    {
        var schema = Parse("""{ "$ref": "#/components/schemas/Pet" }""");
        var components = Parse("""
            {
              "Pet": {
                "type": "object",
                "properties": {
                  "id":   { "type": "integer", "readOnly": true },
                  "name": { "type": "string" }
                }
              }
            }
            """);

        var paths = ReadOnlyPropertyCollector.Collect(schema, components);

        Assert.Contains("/id", paths);
    }

    [Fact]
    public void Handles_cyclic_refs_without_stack_overflow()
    {
        var schema = Parse("""{ "$ref": "#/components/schemas/Node" }""");
        var components = Parse("""
            {
              "Node": {
                "type": "object",
                "properties": {
                  "value":    { "type": "string", "readOnly": true },
                  "parent":   { "$ref": "#/components/schemas/Node" },
                  "children": {
                    "type": "array",
                    "items": { "$ref": "#/components/schemas/Node" }
                  }
                }
              }
            }
            """);

        var paths = ReadOnlyPropertyCollector.Collect(schema, components);

        Assert.Contains("/value", paths);
    }

    [Fact]
    public void Ignores_readOnly_false()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": {
                "id":   { "type": "integer", "readOnly": false },
                "name": { "type": "string" }
              }
            }
            """);

        var paths = ReadOnlyPropertyCollector.Collect(schema, componentsSchemas: null);

        Assert.DoesNotContain("/id", paths);
    }

    [Fact]
    public void Does_not_descend_into_patternProperties_items_or_additionalProperties()
    {
        // The collector currently only walks `properties` and composition
        // keywords. readOnly inside other slots is NOT collected — lock this.
        var schema = Parse("""
            {
              "type": "object",
              "patternProperties": {
                "^S_": {
                  "type": "object",
                  "properties": { "secret": { "type": "string", "readOnly": true } }
                }
              },
              "additionalProperties": {
                "type": "object",
                "properties": { "extra": { "type": "integer", "readOnly": true } }
              }
            }
            """);

        var arraySchema = Parse("""
            {
              "type": "array",
              "items": {
                "type": "object",
                "properties": { "id": { "type": "integer", "readOnly": true } }
              }
            }
            """);

        var pattern = ReadOnlyPropertyCollector.Collect(schema, componentsSchemas: null);
        var array = ReadOnlyPropertyCollector.Collect(arraySchema, componentsSchemas: null);

        Assert.Empty(pattern);
        Assert.Empty(array);
    }

    [Fact]
    public void Returns_empty_set_for_schema_without_properties()
    {
        var schema = Parse("""{ "type": "string" }""");

        var paths = ReadOnlyPropertyCollector.Collect(schema, componentsSchemas: null);

        Assert.Empty(paths);
    }

    [Fact]
    public void Collects_multiple_readOnly_siblings_at_same_level()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": {
                "id":        { "type": "integer", "readOnly": true },
                "createdAt": { "type": "string",  "readOnly": true },
                "name":      { "type": "string" }
              }
            }
            """);

        var paths = ReadOnlyPropertyCollector.Collect(schema, componentsSchemas: null);

        Assert.Contains("/id", paths);
        Assert.Contains("/createdAt", paths);
        Assert.Equal(2, paths.Count);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
