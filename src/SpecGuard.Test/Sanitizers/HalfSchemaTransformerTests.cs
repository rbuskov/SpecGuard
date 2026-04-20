using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SpecGuard.Sanitizers;

namespace SpecGuard.Test.Sanitizers;

public class HalfSchemaTransformerTests
{
    [Fact]
    public async Task Sets_float16_format_for_half()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.Number };
        var context = CreateContext(typeof(Half));

        await new HalfSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

        Assert.Equal("float16", schema.Format);
    }

    [Fact]
    public async Task Nullable_half_is_not_annotated_format_float16()
    {
        // Locks the known limitation: the transformer checks exact type
        // equality (`typeof(Half)`), so `Half?` is not matched and no
        // format is emitted.
        var schema = new OpenApiSchema { Type = JsonSchemaType.Number };
        var context = CreateContext(typeof(Half?));

        await new HalfSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

        Assert.Null(schema.Format);
    }

    [Fact]
    public async Task Does_not_modify_non_half_types()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.Number };
        var context = CreateContext(typeof(double));

        await new HalfSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

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
