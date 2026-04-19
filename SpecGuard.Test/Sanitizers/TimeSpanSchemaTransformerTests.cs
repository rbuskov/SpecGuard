using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SpecGuard.Test.Sanitizers;

public class TimeSpanSchemaTransformerTests
{
    private const string AspNetDefaultPattern =
        @"^-?(\d+\.)?\d{2}:\d{2}:\d{2}(\.\d{1,7})?$";

    private sealed class HasDurations
    {
        [Duration]
        public TimeSpan WithAttribute { get; set; }

        public TimeSpan Plain { get; set; }
    }

    [Fact]
    public async Task Sets_string_duration_for_TimeSpan_with_Duration_attribute()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.String };
        var context = CreateContext(typeof(HasDurations), nameof(HasDurations.WithAttribute));

        await new TimeSpanSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

        Assert.Equal(JsonSchemaType.String, schema.Type);
        Assert.Equal("duration", schema.Format);
    }

    [Fact]
    public async Task Strips_aspnet_default_pattern_when_applying_duration_format()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = AspNetDefaultPattern };
        var context = CreateContext(typeof(HasDurations), nameof(HasDurations.WithAttribute));

        await new TimeSpanSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

        Assert.Null(schema.Pattern);
    }

    [Fact]
    public async Task Preserves_custom_pattern_when_applying_duration_format()
    {
        const string custom = "^PT\\d+H$";
        var schema = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = custom };
        var context = CreateContext(typeof(HasDurations), nameof(HasDurations.WithAttribute));

        await new TimeSpanSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

        Assert.Equal(custom, schema.Pattern);
    }

    [Fact]
    public async Task Leaves_TimeSpan_without_attribute_untouched()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = AspNetDefaultPattern };
        var context = CreateContext(typeof(HasDurations), nameof(HasDurations.Plain));

        await new TimeSpanSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

        Assert.Null(schema.Format);
        Assert.Equal(AspNetDefaultPattern, schema.Pattern);
    }

    [Fact]
    public async Task Leaves_top_level_TimeSpan_without_property_info_untouched()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.String };
        var context = CreateContext(typeof(TimeSpan), propertyName: null);

        await new TimeSpanSchemaTransformer().TransformAsync(schema, context, CancellationToken.None);

        Assert.Null(schema.Format);
    }

    private static OpenApiSchemaTransformerContext CreateContext(Type declaringType, string? propertyName)
    {
        var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var typeInfo = options.GetTypeInfo(declaringType);

        JsonPropertyInfo? propertyInfo = null;
        Type schemaType = declaringType;
        if (propertyName is not null)
        {
            propertyInfo = typeInfo.Properties.First(p => p.Name == propertyName);
            schemaType = propertyInfo.PropertyType;
        }

        return new OpenApiSchemaTransformerContext
        {
            DocumentName = "v1",
            JsonTypeInfo = options.GetTypeInfo(schemaType),
            JsonPropertyInfo = propertyInfo,
            ParameterDescription = null,
            ApplicationServices = null!,
        };
    }
}
