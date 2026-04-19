using System.Text.Json;
using Json.Schema;
using Microsoft.AspNetCore.Http;
using SpecGuard.Validators.ValidationResults;

namespace SpecGuard.Validators;

internal class JsonBodyValidator : IRequestValidator
{
    private readonly SpecGuardOptions options;

    public JsonBodyValidator(SpecGuardOptions options) => this.options = options;

    public JsonBodyValidator() : this(new SpecGuardOptions()) { }
    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "get", "put", "post", "delete", "options", "head", "patch", "trace",
    };

    private static readonly JsonSchema AcceptAnySchema = JsonSchema.FromText("true");

    private static readonly IReadOnlyList<ValidationErrorResult.ValidationError> EmptyErrors =
        Array.Empty<ValidationErrorResult.ValidationError>();

    private volatile OperationSchema[] operations = [];

    public void Initialize(JsonDocument openApiSpec)
    {
        var root = openApiSpec.RootElement;

        if (!root.TryGetProperty("paths", out var paths) ||
            paths.ValueKind != JsonValueKind.Object)
        {
            operations = [];
            return;
        }

        var componentsSchemas = TryGetComponentsSchemas(root);
        var built = new List<OperationSchema>();

        foreach (var pathEntry in paths.EnumerateObject())
        {
            if (pathEntry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var matcher = new RoutePatternMatcher(pathEntry.Name);

            foreach (var operationEntry in pathEntry.Value.EnumerateObject())
            {
                if (!HttpMethods.Contains(operationEntry.Name))
                {
                    continue;
                }

                var (schema, bodyRequired, readOnlyPaths) = BuildSchema(operationEntry.Value, componentsSchemas, options.RejectAdditionalProperties);

                built.Add(new OperationSchema(
                    operationEntry.Name.ToUpperInvariant(),
                    matcher,
                    schema,
                    bodyRequired,
                    readOnlyPaths));
            }
        }

        built.Sort((a, b) => b.Matcher.LiteralSegmentCount.CompareTo(a.Matcher.LiteralSegmentCount));
        operations = built.ToArray();
    }

    private static (JsonSchema Schema, bool BodyRequired, HashSet<string> ReadOnlyPaths) BuildSchema(JsonElement operationElement, JsonElement? componentsSchemas, bool rejectAdditionalProperties)
    {
        if (!TryGetJsonRequestBodySchema(operationElement, out var bodySchema, out var bodyRequired))
        {
            return (AcceptAnySchema, false, []);
        }

        var built = OpenApiSchemaBuilder.Build(bodySchema, componentsSchemas, rejectAdditionalProperties);
        var readOnlyPaths = ReadOnlyPropertyCollector.Collect(bodySchema, componentsSchemas);
        return (JsonSchema.FromText(built.ToJsonString()), bodyRequired, readOnlyPaths);
    }

    private static JsonElement? TryGetComponentsSchemas(JsonElement root)
    {
        if (!root.TryGetProperty("components", out var components) ||
            components.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!components.TryGetProperty("schemas", out var schemas) ||
            schemas.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return schemas;
    }

    private static bool TryGetJsonRequestBodySchema(JsonElement operationElement, out JsonElement schema, out bool bodyRequired)
    {
        schema = default;
        bodyRequired = false;

        if (!operationElement.TryGetProperty("requestBody", out var requestBody) ||
            requestBody.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        bodyRequired = requestBody.TryGetProperty("required", out var requiredProp) &&
                       requiredProp.ValueKind == JsonValueKind.True;

        if (!requestBody.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!content.TryGetProperty("application/json", out var jsonContent) ||
            jsonContent.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!jsonContent.TryGetProperty("schema", out schema) ||
            schema.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return true;
    }

    public ValueTask<IReadOnlyList<ValidationErrorResult.ValidationError>> ValidateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var operation = ResolveOperation(request);

        var parsedBody = context.Items.TryGetValue(SpecGuardMiddleware.ParsedBodyKey, out var item)
            ? (JsonElement?)item
            : null;
        var bodyIsEmpty = context.Items.ContainsKey(SpecGuardMiddleware.BodyEmptyKey);

        if (parsedBody is null && !bodyIsEmpty)
        {
            // No JSON content-type — middleware didn't attempt to parse
            if (operation is { BodyRequired: true })
            {
                return ValueTask.FromResult<IReadOnlyList<ValidationErrorResult.ValidationError>>(
                    [new ValidationErrorResult.ValidationError(
                        "A request body is required but none was provided.",
                        "body",
                        "")]);
            }

            return ValueTask.FromResult(EmptyErrors);
        }

        if (bodyIsEmpty)
        {
            if (operation is { BodyRequired: true })
            {
                return ValueTask.FromResult<IReadOnlyList<ValidationErrorResult.ValidationError>>(
                    [new ValidationErrorResult.ValidationError(
                        "A request body is required but none was provided.",
                        "body",
                        "")]);
            }

            return ValueTask.FromResult(EmptyErrors);
        }

        if (operation is null || parsedBody is not { } body)
        {
            return ValueTask.FromResult(EmptyErrors);
        }

        if (operation.ReadOnlyPaths.Count > 0)
        {
            var readOnlyErrors = FindReadOnlyViolations(body, "", operation.ReadOnlyPaths);
            if (readOnlyErrors.Count > 0)
            {
                return ValueTask.FromResult<IReadOnlyList<ValidationErrorResult.ValidationError>>(readOnlyErrors);
            }
        }

        var evaluation = operation.Schema.Evaluate(body, EvaluationOptions);
        if (evaluation.IsValid)
        {
            return ValueTask.FromResult(EmptyErrors);
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationErrorResult.ValidationError>>(CollectErrors(evaluation));
    }

    private static readonly EvaluationOptions EvaluationOptions = new()
    {
        OutputFormat = OutputFormat.List,
    };

    private static ValidationErrorResult.ValidationError[] CollectErrors(EvaluationResults evaluation)
    {
        var raw = EvaluationErrorFilter.Collect(evaluation);

        if (raw.Count > 0)
        {
            return raw.ConvertAll(e => new ValidationErrorResult.ValidationError(e.Message, "body", e.Path)).ToArray();
        }

        // Some keywords (notably `not` and a multi-match `oneOf`) fail without
        // producing a leaf-level error message under List output mode. Fall back
        // to a synthesized error so the client never sees an empty errors list.
        var invalidNode = FindInvalidLeaf(evaluation) ?? evaluation;
        var keyword = LastEvaluationKeyword(invalidNode.EvaluationPath.ToString());
        var message = keyword is null
            ? "Value does not match the schema"
            : $"Value does not match schema keyword '{keyword}'";
        return [new ValidationErrorResult.ValidationError(message, "body", invalidNode.InstanceLocation.ToString())];
    }

    private static EvaluationResults? FindInvalidLeaf(EvaluationResults node)
    {
        if (node.IsValid)
        {
            return null;
        }

        if (node.Details is { Count: > 0 } details)
        {
            foreach (var detail in details)
            {
                var child = FindInvalidLeaf(detail);
                if (child is not null)
                {
                    return child;
                }
            }
        }

        return node;
    }

    private static string? LastEvaluationKeyword(string pointer)
    {
        // Pointer is of the form "/keyword/0/nested" — walk segments from the end
        // and return the first non-numeric one.
        if (string.IsNullOrEmpty(pointer))
        {
            return null;
        }

        var segments = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            if (!int.TryParse(segments[i], out _))
            {
                return segments[i];
            }
        }

        return null;
    }

    private OperationSchema? ResolveOperation(HttpRequest request)
    {
        foreach (var operation in operations)
        {
            if (string.Equals(operation.Method, request.Method, StringComparison.OrdinalIgnoreCase) &&
                operation.Matcher.IsMatch(request.Path))
            {
                return operation;
            }
        }

        return null;
    }

    private static List<ValidationErrorResult.ValidationError> FindReadOnlyViolations(
        JsonElement element, string prefix, HashSet<string> readOnlyPaths)
    {
        var errors = new List<ValidationErrorResult.ValidationError>();
        CollectReadOnlyViolations(element, prefix, readOnlyPaths, errors);
        return errors;
    }

    private static void CollectReadOnlyViolations(
        JsonElement element, string prefix, HashSet<string> readOnlyPaths,
        List<ValidationErrorResult.ValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var prop in element.EnumerateObject())
        {
            var path = prefix + "/" + prop.Name;

            if (readOnlyPaths.Contains(path))
            {
                errors.Add(new ValidationErrorResult.ValidationError(
                    $"Property '{prop.Name}' is read-only and must not be included in request bodies",
                    "body",
                    path));
            }

            CollectReadOnlyViolations(prop.Value, path, readOnlyPaths, errors);
        }
    }

    private sealed record OperationSchema(string Method, RoutePatternMatcher Matcher, JsonSchema Schema, bool BodyRequired, HashSet<string> ReadOnlyPaths);
}
