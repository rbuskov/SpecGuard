using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SpecGuard.TicTacToeApi.OpenApi;

// Minimal API emits `{type: integer}` for int path params.  The Tic-Tac-Toe
// board is 3×3, so surface the range in the generated OpenAPI so Turbine can
// reject out-of-range coordinates before the request hits the endpoint.
internal sealed class CoordinateRangeTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (operation.Parameters is null)
        {
            return Task.CompletedTask;
        }

        foreach (var parameter in operation.Parameters)
        {
            if (parameter.Name is "row" or "column" && parameter.Schema is OpenApiSchema schema)
            {
                schema.Minimum = "1";
                schema.Maximum = "3";
            }
        }

        return Task.CompletedTask;
    }
}
