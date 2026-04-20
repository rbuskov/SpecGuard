using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SpecGuard.TicTacToeApi.Models;

namespace SpecGuard.TicTacToeApi.OpenApi;

// The board is fixed at 3×3, but Minimal API emits `Mark[][]` as an
// unbounded jagged array. Attributes can't reach the inner rows (jagged
// elements have no property to decorate), so constrain both dimensions
// here when the board schema is generated.
internal sealed class BoardShapeTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type != typeof(Mark[][]))
        {
            return Task.CompletedTask;
        }

        schema.MinItems = 3;
        schema.MaxItems = 3;

        if (schema.Items is OpenApiSchema inner)
        {
            inner.MinItems = 3;
            inner.MaxItems = 3;
        }

        return Task.CompletedTask;
    }
}
