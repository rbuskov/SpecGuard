using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SpecGuard.Sanitizers;

/// <summary>
/// Emits <c>format: "email"</c> for string properties decorated with
/// <see cref="EmailAddressAttribute"/>. ASP.NET Core's OpenAPI generator does
/// not project this annotation into the schema by default.
/// </summary>
internal sealed class EmailAddressSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(string)
            && context.JsonPropertyInfo?.AttributeProvider is { } provider
            && provider.IsDefined(typeof(EmailAddressAttribute), inherit: true))
        {
            schema.Format = "email";
        }

        return Task.CompletedTask;
    }
}
