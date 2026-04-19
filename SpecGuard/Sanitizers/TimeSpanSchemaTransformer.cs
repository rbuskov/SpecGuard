using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SpecGuard.Sanitizers;

/// <summary>
/// Registers the ISO 8601 duration schema for TimeSpan properties
/// decorated with <see cref="OpenApiDurationAttribute"/>.
/// </summary>
internal sealed class TimeSpanSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(TimeSpan)
            && context.JsonPropertyInfo?.AttributeProvider is { } provider
            && provider.IsDefined(typeof(OpenApiDurationAttribute), inherit: false))
        {
            schema.Type = JsonSchemaType.String;
            schema.Format = "duration";
            if (schema.Pattern == @"^-?(\d+\.)?\d{2}:\d{2}:\d{2}(\.\d{1,7})?$")
            {
                schema.Pattern = null;
            }
        }

        return Task.CompletedTask;
    }
}
