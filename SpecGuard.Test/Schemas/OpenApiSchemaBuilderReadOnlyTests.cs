using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpecGuard.Test.Schemas;

public class OpenApiSchemaBuilderReadOnlyTests
{
    [Fact]
    public void When_all_required_are_readOnly_required_is_removed_entirely()
    {
        var schema = Parse("""
            {
              "type": "object",
              "required": ["id", "createdAt"],
              "properties": {
                "id":        { "type": "integer", "readOnly": true },
                "createdAt": { "type": "string",  "readOnly": true }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);

        Assert.Null(result["required"]);
    }

    [Fact]
    public void When_some_required_are_readOnly_only_those_names_are_stripped()
    {
        var schema = Parse("""
            {
              "type": "object",
              "required": ["id", "name"],
              "properties": {
                "id":   { "type": "integer", "readOnly": true },
                "name": { "type": "string" }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);

        var required = Assert.IsType<JsonArray>(result["required"]);
        var only = Assert.Single(required);
        Assert.Equal("name", only!.GetValue<string>());
    }

    [Fact]
    public void Required_inside_components_has_readOnly_entries_stripped()
    {
        var operationSchema = Parse("""{ "$ref": "#/components/schemas/Pet" }""");
        var components = Parse("""
            {
              "Pet": {
                "type": "object",
                "required": ["id", "name"],
                "properties": {
                  "id":   { "type": "integer", "readOnly": true },
                  "name": { "type": "string" }
                }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(operationSchema, components);
        var petDef = result["$defs"]!["Pet"]!;
        var required = Assert.IsType<JsonArray>(petDef["required"]);

        var only = Assert.Single(required);
        Assert.Equal("name", only!.GetValue<string>());
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
