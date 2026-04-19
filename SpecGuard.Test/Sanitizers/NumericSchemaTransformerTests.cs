using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SpecGuard.Sanitizers;

namespace SpecGuard.Test.Sanitizers;

public class NumericSchemaTransformerTests
{
    private const string GeneratedIntegerPattern = @"^-?(?:0|[1-9]\d*)$";

    [Fact]
    public async Task Default_strips_string_from_numeric_type_union()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Integer | JsonSchemaType.String,
            Pattern = GeneratedIntegerPattern,
        };

        await new NumericSchemaTransformer(new SpecGuardOptions())
            .TransformAsync(schema, CreateContext(typeof(int)), CancellationToken.None);

        Assert.Equal(JsonSchemaType.Integer, schema.Type);
        Assert.Null(schema.Pattern);
    }

    [Fact]
    public async Task AllowStringNumerics_preserves_type_union_and_pattern()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Integer | JsonSchemaType.String,
            Pattern = GeneratedIntegerPattern,
        };

        await new NumericSchemaTransformer(new SpecGuardOptions { AllowStringNumerics = true })
            .TransformAsync(schema, CreateContext(typeof(int)), CancellationToken.None);

        Assert.Equal(JsonSchemaType.Integer | JsonSchemaType.String, schema.Type);
        Assert.Equal(GeneratedIntegerPattern, schema.Pattern);
    }

    [Fact]
    public async Task Non_numeric_schemas_are_untouched_regardless_of_option()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.String };

        await new NumericSchemaTransformer(new SpecGuardOptions { AllowStringNumerics = true })
            .TransformAsync(schema, CreateContext(typeof(string)), CancellationToken.None);

        Assert.Equal(JsonSchemaType.String, schema.Type);
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
