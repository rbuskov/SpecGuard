using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpecGuard.Test.Schemas;

public class OpenApiSchemaBuilderEdgeTests
{
    // ── Nullable composition collapse edges ──────────────────────────────

    [Fact]
    public void OneOf_with_three_branches_including_null_is_not_collapsed()
    {
        var schema = Parse("""
            {
              "oneOf": [
                { "type": "null" },
                { "type": "string" },
                { "type": "integer" }
              ]
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);

        Assert.NotNull(result["oneOf"]);
        Assert.Null(result["anyOf"]);
    }

    [Fact]
    public void OneOf_two_branches_without_null_is_not_collapsed()
    {
        var schema = Parse("""
            {
              "oneOf": [
                { "type": "string" },
                { "type": "integer" }
              ]
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);

        Assert.NotNull(result["oneOf"]);
        Assert.Null(result["anyOf"]);
    }

    [Fact]
    public void Nullable_oneOf_inside_allOf_is_still_collapsed()
    {
        // The collapse rule applies to any `oneOf` node in the tree —
        // including one nested inside `allOf`.
        var schema = Parse("""
            {
              "allOf": [
                {
                  "oneOf": [
                    { "type": "null" },
                    { "type": "string" }
                  ]
                }
              ]
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var inner = (JsonObject)((JsonArray)result["allOf"]!)[0]!;

        Assert.Null(inner["oneOf"]);
        Assert.NotNull(inner["anyOf"]);
    }

    [Fact]
    public void Explicit_mapping_entry_outside_oneOf_is_still_emitted()
    {
        // A mapping entry like "foo" → "#/components/schemas/Foo" is
        // included in the if/then chain even when Foo is not referenced
        // from any oneOf/anyOf branch.
        var schema = Parse("""
            {
              "oneOf": [
                { "$ref": "#/components/schemas/A" }
              ],
              "discriminator": {
                "propertyName": "kind",
                "mapping": {
                  "a": "#/components/schemas/A",
                  "b": "#/components/schemas/B"
                }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var chain = (JsonObject)((JsonArray)result["allOf"]!)[0]!;

        // First entry: "a" → #/$defs/A
        Assert.Equal("a", chain["if"]!["properties"]!["kind"]!["const"]!.GetValue<string>());
        // Second entry (in else): "b" → #/$defs/B
        var elseBranch = (JsonObject)chain["else"]!;
        Assert.Equal("b", elseBranch["if"]!["properties"]!["kind"]!["const"]!.GetValue<string>());
    }

    [Fact]
    public void OneOf_with_type_array_null_member_is_not_collapsed()
    {
        // IsNullType only matches `type: "null"` (string), not
        // `type: ["null", "string"]` (array).
        var schema = Parse("""
            {
              "oneOf": [
                { "type": ["null", "string"] },
                { "$ref": "#/components/schemas/Status" }
              ]
            }
            """);

        var components = Parse("""{ "Status": { "type": "string" } }""");

        var result = OpenApiSchemaBuilder.Build(schema, components);

        Assert.NotNull(result["oneOf"]);
        Assert.Null(result["anyOf"]);
    }

    // ── Discriminator edges ───────────────────────────────────────────────

    [Fact]
    public void Discriminator_with_implicit_mapping_infers_from_anyOf_refs()
    {
        var schema = Parse("""
            {
              "anyOf": [
                { "$ref": "#/components/schemas/Cat" },
                { "$ref": "#/components/schemas/Dog" }
              ],
              "discriminator": { "propertyName": "petType" }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);

        Assert.Null(result["discriminator"]);
        var allOf = Assert.IsType<JsonArray>(result["allOf"]);
        var chain = Assert.IsType<JsonObject>(allOf[0]);
        Assert.Equal("Cat", chain["if"]!["properties"]!["petType"]!["const"]!.GetValue<string>());
    }

    [Fact]
    public void Discriminator_chain_terminates_with_else_false_sentinel()
    {
        var schema = Parse("""
            {
              "discriminator": {
                "propertyName": "kind",
                "mapping": { "a": "#/components/schemas/A" }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var chain = (JsonObject)((JsonArray)result["allOf"]!)[0]!;

        var elseNode = chain["else"];
        Assert.NotNull(elseNode);
        // else is a boolean `false`, which JSON Schema treats as "nothing matches".
        Assert.False(elseNode!.GetValue<bool>());
    }

    [Fact]
    public void Discriminator_without_mapping_or_branches_emits_no_chain()
    {
        var schema = Parse("""
            {
              "type": "object",
              "discriminator": { "propertyName": "kind" }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);

        Assert.Null(result["discriminator"]);
        Assert.Null(result["allOf"]);
    }

    [Fact]
    public void Discriminator_external_ref_is_kept_verbatim()
    {
        var schema = Parse("""
            {
              "discriminator": {
                "propertyName": "kind",
                "mapping": { "ext": "https://example.com/schema.json#/Ext" }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var chain = (JsonObject)((JsonArray)result["allOf"]!)[0]!;

        Assert.Equal(
            "https://example.com/schema.json#/Ext",
            chain["then"]!["$ref"]!.GetValue<string>());
    }

    [Fact]
    public void Discriminator_preserves_existing_allOf_branches()
    {
        var schema = Parse("""
            {
              "allOf": [
                { "$ref": "#/components/schemas/Base" }
              ],
              "discriminator": {
                "propertyName": "kind",
                "mapping": { "a": "#/components/schemas/A" }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var allOf = Assert.IsType<JsonArray>(result["allOf"]);

        Assert.Equal(2, allOf.Count);
        // Original $ref branch is preserved as the first allOf element.
        Assert.Equal("#/$defs/Base", allOf[0]!["$ref"]!.GetValue<string>());
    }

    // ── Format handling edges ────────────────────────────────────────────

    [Fact]
    public void Int64_format_emits_long_minmax()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": { "v": { "type": "integer", "format": "int64" } }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var v = result["properties"]!["v"]!;

        Assert.Equal(long.MinValue, v["minimum"]!.GetValue<long>());
        Assert.Equal(long.MaxValue, v["maximum"]!.GetValue<long>());
    }

    [Fact]
    public void Double_format_emits_double_minmax()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": { "v": { "type": "number", "format": "double" } }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var v = result["properties"]!["v"]!;

        Assert.Equal(double.MinValue, v["minimum"]!.GetValue<double>());
        Assert.Equal(double.MaxValue, v["maximum"]!.GetValue<double>());
    }

    [Fact]
    public void Non_numeric_openapi_formats_do_not_emit_min_or_max_on_string_types()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": {
                "bin": { "type": "string", "format": "binary" },
                "pwd": { "type": "string", "format": "password" }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        foreach (var p in result["properties"]!.AsObject())
        {
            Assert.Null(p.Value!["minimum"]);
            Assert.Null(p.Value!["maximum"]);
        }
    }

    [Fact]
    public void Uint64_format_emits_ulong_maxvalue_without_overflow()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": { "v": { "type": "integer", "format": "uint64" } }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var v = result["properties"]!["v"]!;

        Assert.Equal(ulong.MaxValue, v["maximum"]!.GetValue<ulong>());
    }

    [Fact]
    public void Unknown_format_on_string_type_is_preserved()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": { "v": { "type": "string", "format": "made-up" } }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var v = result["properties"]!["v"]!;

        Assert.Equal("made-up", v["format"]!.GetValue<string>());
    }

    // ── $ref rewriting edges ─────────────────────────────────────────────

    [Fact]
    public void External_ref_is_preserved_verbatim_not_rewritten()
    {
        var schema = Parse("""{ "$ref": "https://example.com/schema.json#/Foo" }""");

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);

        Assert.Equal("https://example.com/schema.json#/Foo", result["$ref"]!.GetValue<string>());
    }

    [Fact]
    public void Ref_already_in_defs_form_is_left_alone()
    {
        // A pre-existing `#/$defs/...` ref should pass through verbatim —
        // the rewriting only fires for `#/components/schemas/...`.
        var schema = Parse("""{ "$ref": "#/$defs/Pet" }""");

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);

        Assert.Equal("#/$defs/Pet", result["$ref"]!.GetValue<string>());
    }

    [Fact]
    public void Ref_with_sibling_keywords_preserves_siblings()
    {
        var schema = Parse("""
            {
              "$ref": "#/components/schemas/Pet",
              "description": "A pet"
            }
            """);
        var components = Parse("""{ "Pet": { "type": "object" } }""");

        var result = OpenApiSchemaBuilder.Build(schema, components);

        Assert.Equal("#/$defs/Pet", result["$ref"]!.GetValue<string>());
        Assert.Equal("A pet", result["description"]!.GetValue<string>());
    }

    // ── Top-level schema shape edges ─────────────────────────────────────

    [Fact]
    public void Boolean_schema_false_at_operation_level_is_wrapped_in_allOf()
    {
        var schema = Parse("false");

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);

        var allOf = Assert.IsType<JsonArray>(result["allOf"]);
        Assert.Single(allOf);
        Assert.False(allOf[0]!.GetValue<bool>());
    }

    [Fact]
    public void Empty_object_schema_passes_through_with_augmentations()
    {
        var schema = Parse("{}");

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);

        Assert.NotNull(result["$schema"]);
        Assert.NotNull(result["$defs"]);
    }

    // ── writeOnly ────────────────────────────────────────────────────────

    [Fact]
    public void WriteOnly_is_stripped_from_the_produced_schema()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": {
                "password": { "type": "string", "writeOnly": true }
              }
            }
            """);

        var result = OpenApiSchemaBuilder.Build(schema, componentsSchemas: null);
        var password = result["properties"]!["password"]!;

        Assert.Null(password["writeOnly"]);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
