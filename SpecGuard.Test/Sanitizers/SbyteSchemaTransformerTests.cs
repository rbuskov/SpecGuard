using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SpecGuard.Sanitizers;

namespace SpecGuard.Test.Sanitizers;

public class SbyteSchemaTransformerTests
{
    [Fact]
    public async Task Sets_int8_format_for_sbyte()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.Integer };
        var context = CreateContext(typeof(sbyte));

        await new SbyteSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

        Assert.Equal("int8", schema.Format);
    }

    [Fact]
    public async Task Nullable_sbyte_is_not_annotated_format_int8()
    {
        // Locks the known limitation: the transformer checks the exact type
        // equality (`typeof(sbyte)`), so `sbyte?` (Nullable<sbyte>) is not
        // matched and no format is emitted.
        var schema = new OpenApiSchema { Type = JsonSchemaType.Integer };
        var context = CreateContext(typeof(sbyte?));

        await new SbyteSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

        Assert.Null(schema.Format);
    }

    [Fact]
    public async Task Does_not_modify_non_sbyte_types()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.Integer };
        var context = CreateContext(typeof(int));

        await new SbyteSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

        Assert.Null(schema.Format);
    }

    private static OpenApiSchemaTransformerContext CreateContext(Type type)
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        return new OpenApiSchemaTransformerContext
        {
            DocumentName = "v1",
            JsonTypeInfo = options.GetTypeInfo(type),
            JsonPropertyInfo = null,
            ParameterDescription = null,
            ApplicationServices = null!,
        };
    }
}
