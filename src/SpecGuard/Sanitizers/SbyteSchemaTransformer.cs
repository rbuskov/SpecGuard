using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SpecGuard.Sanitizers;

/// <summary>
/// Sets the int8 format for sbyte schemas, which ASP.NET Core does not annotate with a format.
/// </summary>
internal sealed class SbyteSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(sbyte))
        {
            schema.Format = "int8";
        }

        return Task.CompletedTask;
    }
}
