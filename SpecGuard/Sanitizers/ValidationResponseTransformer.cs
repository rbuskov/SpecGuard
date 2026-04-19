using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SpecGuard.Sanitizers;

/// <summary>
/// Adds <c>400</c> and <c>422</c> responses to every operation that SpecGuard
/// will actually validate at runtime, mirroring the rules in
/// <c>SpecGuardMiddleware</c>, <c>JsonBodyValidator</c>, and
/// <c>ParameterValidator</c>. Existing <c>400</c>/<c>422</c> entries are left
/// untouched so hand-authored responses are never clobbered.
/// </summary>
internal sealed class ValidationResponseTransformer : IOpenApiOperationTransformer
{
    private const string ProblemJsonMediaType = "application/problem+json";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var hasJsonBody = HasJsonRequestBody(operation);
        var hasParameters = operation.Parameters is { Count: > 0 };

        if (!hasJsonBody && !hasParameters)
        {
            return Task.CompletedTask;
        }

        operation.Responses ??= new OpenApiResponses();

        if (hasJsonBody && !operation.Responses.ContainsKey("400"))
        {
            operation.Responses["400"] = BuildMalformedJsonResponse();
        }

        if (!operation.Responses.ContainsKey("422"))
        {
            operation.Responses["422"] = BuildValidationFailedResponse();
        }

        return Task.CompletedTask;
    }

    private static bool HasJsonRequestBody(OpenApiOperation operation)
    {
        var content = operation.RequestBody?.Content;
        if (content is null)
        {
            return false;
        }

        foreach (var mediaType in content.Keys)
        {
            if (IsJsonMediaType(mediaType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsJsonMediaType(string mediaType)
    {
        var semicolon = mediaType.IndexOf(';');
        var value = semicolon >= 0 ? mediaType[..semicolon].Trim() : mediaType.Trim();

        return value.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static OpenApiResponse BuildMalformedJsonResponse() => new()
    {
        Description = "Malformed JSON in the request body.",
        Content = new Dictionary<string, OpenApiMediaType>
        {
            [ProblemJsonMediaType] = new() { Schema = BuildProblemDetailsSchema() },
        },
    };

    private static OpenApiResponse BuildValidationFailedResponse() => new()
    {
        Description = "One or more validation errors occurred.",
        Content = new Dictionary<string, OpenApiMediaType>
        {
            [ProblemJsonMediaType] = new() { Schema = BuildValidationProblemDetailsSchema() },
        },
    };

    private static OpenApiSchema BuildProblemDetailsSchema() => new()
    {
        Type = JsonSchemaType.Object,
        Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["type"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
        },
    };

    private static OpenApiSchema BuildValidationProblemDetailsSchema()
    {
        var schema = BuildProblemDetailsSchema();
        schema.Properties!["errors"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Array,
            Items = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["message"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    ["in"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    ["path"] = new OpenApiSchema { Type = JsonSchemaType.String },
                },
                Required = new HashSet<string> { "message", "in", "path" },
            },
        };
        return schema;
    }
}
