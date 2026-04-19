using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SpecGuard.Sanitizers;

/// <summary>
/// Sets the float16 format for Half schemas, which ASP.NET Core does not annotate with a format.
/// </summary>
internal sealed class HalfSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(Half))
        {
            schema.Format = "float16";
        }

        return Task.CompletedTask;
    }
}
