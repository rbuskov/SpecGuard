using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.AspNetCore.Http;
using SpecGuard.Validators.ValidationResults;

namespace SpecGuard.Validators;

internal class ParameterValidator : IRequestValidator
{
    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "get", "put", "post", "delete", "options", "head", "patch", "trace",
    };

    private static readonly EvaluationOptions EvaluationOptions = new()
    {
        OutputFormat = OutputFormat.List,
    };

    private const string ComponentsParametersPrefix = "#/components/parameters/";
    private const string ComponentsSchemasPrefix = "#/components/schemas/";

    private volatile OperationParameters[] operations = [];

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
        var componentsParameters = TryGetComponentsParameters(root);
        var built = new List<OperationParameters>();

        foreach (var pathEntry in paths.EnumerateObject())
        {
            if (pathEntry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var template = pathEntry.Name;
            var matcher = new RoutePatternMatcher(template);
            var pathLevelParameters = CollectParameters(pathEntry.Value, componentsParameters);

            foreach (var operationEntry in pathEntry.Value.EnumerateObject())
            {
                if (!HttpMethods.Contains(operationEntry.Name))
                {
                    continue;
                }

                var operationLevelParameters = CollectParameters(operationEntry.Value, componentsParameters);
                var merged = MergeParameters(pathLevelParameters, operationLevelParameters);

                var infos = new List<ParameterInfo>(merged.Count);
                foreach (var parameter in merged)
                {
                    if (TryBuildParameterInfo(parameter, componentsSchemas, out var info))
                    {
                        infos.Add(info);
                    }
                }

                built.Add(new OperationParameters(
                    operationEntry.Name.ToUpperInvariant(),
                    matcher,
                    template,
                    infos));
            }
        }

        built.Sort((a, b) => b.Matcher.LiteralSegmentCount.CompareTo(a.Matcher.LiteralSegmentCount));
        operations = built.ToArray();
    }

    private static readonly IReadOnlyList<ValidationErrorResult.ValidationError> EmptyErrors =
        Array.Empty<ValidationErrorResult.ValidationError>();

    public ValueTask<IReadOnlyList<ValidationErrorResult.ValidationError>> ValidateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var operation = ResolveOperation(request);

        if (operation is null)
        {
            return ValueTask.FromResult(EmptyErrors);
        }

        var pathValues = ExtractPathValues(operation.Template, request.Path);
        var errors = new List<ValidationErrorResult.ValidationError>();

        foreach (var parameter in operation.Parameters)
        {
            ValidateParameter(parameter, request, pathValues, errors);
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationErrorResult.ValidationError>>(errors);
    }

    private OperationParameters? ResolveOperation(HttpRequest request)
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

    private static void ValidateParameter(
        ParameterInfo parameter,
        HttpRequest request,
        Dictionary<string, string> pathValues,
        List<ValidationErrorResult.ValidationError> errors)
    {
        // deepObject parameters use bracket notation (e.g. filter[status]=active)
        // and need a completely different value-extraction path.
        if (parameter.In == "query" && parameter.Style == "deepObject")
        {
            ValidateDeepObjectParameter(parameter, request, errors);
            return;
        }

        var rawValues = GetRawValues(parameter, request, pathValues);

        if (rawValues is null)
        {
            // Path params that don't resolve mean the route didn't match the
            // template — ASP.NET Core routing will 404 before we get here, so
            // only non-path params can legitimately be missing.
            if (parameter.Required && parameter.In != "path")
            {
                errors.Add(new ValidationErrorResult.ValidationError(
                    $"Missing required {parameter.In} parameter '{parameter.Name}'",
                    parameter.In,
                    parameter.Name));
            }

            return;
        }

        if (parameter.In == "query" &&
            !parameter.AllowEmptyValue &&
            rawValues.Count == 1 &&
            rawValues[0].Length == 0)
        {
            errors.Add(new ValidationErrorResult.ValidationError(
                $"Query parameter '{parameter.Name}' must not be empty",
                parameter.In,
                parameter.Name));
            return;
        }

        if (parameter.Schema is null)
        {
            return;
        }

        JsonElement instance;
        if (parameter.ContentEncoded)
        {
            var raw = rawValues.Count > 0 ? rawValues[0] : string.Empty;
            try
            {
                instance = JsonDocument.Parse(raw).RootElement;
            }
            catch (JsonException)
            {
                errors.Add(new ValidationErrorResult.ValidationError(
                    $"Parameter '{parameter.Name}' has invalid JSON content",
                    parameter.In,
                    parameter.Name));
                return;
            }
        }
        else
        {
            var node = Deserialize(parameter, rawValues);
            instance = JsonSerializer.SerializeToElement(node);
        }

        var evaluation = parameter.Schema.Evaluate(instance, EvaluationOptions);

        if (evaluation.IsValid)
        {
            return;
        }

        foreach (var message in CollectErrorMessages(evaluation))
        {
            errors.Add(new ValidationErrorResult.ValidationError(message, parameter.In, parameter.Name));
        }
    }

    private static void ValidateDeepObjectParameter(
        ParameterInfo parameter,
        HttpRequest request,
        List<ValidationErrorResult.ValidationError> errors)
    {
        var prefix = parameter.Name + "[";
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in request.Query.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal) && key.EndsWith(']'))
            {
                var propertyName = key[prefix.Length..^1];
                if (request.Query.TryGetValue(key, out var values) && values.Count > 0)
                {
                    properties[propertyName] = values[0] ?? string.Empty;
                }
            }
        }

        if (properties.Count == 0)
        {
            if (parameter.Required)
            {
                errors.Add(new ValidationErrorResult.ValidationError(
                    $"Missing required {parameter.In} parameter '{parameter.Name}'",
                    parameter.In,
                    parameter.Name));
            }
            return;
        }

        if (parameter.Schema is null)
        {
            return;
        }

        var obj = BuildObjectNode(properties, parameter.SchemaElement, parameter.ComponentsSchemas);
        var instance = JsonSerializer.SerializeToElement(obj);
        var evaluation = parameter.Schema.Evaluate(instance, EvaluationOptions);

        if (evaluation.IsValid)
        {
            return;
        }

        foreach (var message in CollectErrorMessages(evaluation))
        {
            errors.Add(new ValidationErrorResult.ValidationError(message, parameter.In, parameter.Name));
        }
    }

    private static List<string>? GetRawValues(
        ParameterInfo parameter,
        HttpRequest request,
        Dictionary<string, string> pathValues)
    {
        switch (parameter.In)
        {
            case "path":
                return pathValues.TryGetValue(parameter.Name, out var pathValue)
                    ? new List<string> { pathValue }
                    : null;

            case "query":
                if (!request.Query.TryGetValue(parameter.Name, out var queryValues))
                {
                    return null;
                }
                return queryValues.Select(value => value ?? string.Empty).ToList();

            case "header":
                if (!request.Headers.TryGetValue(parameter.Name, out var headerValues))
                {
                    return null;
                }
                return headerValues.Select(value => value ?? string.Empty).ToList();

            case "cookie":
                return request.Cookies.TryGetValue(parameter.Name, out var cookieValue)
                    ? new List<string> { cookieValue }
                    : null;

            default:
                return null;
        }
    }

    private static JsonNode? Deserialize(ParameterInfo parameter, List<string> rawValues)
    {
        var schemaType = parameter.SchemaElement is { } schema ? GetSchemaType(schema) : null;

        if (schemaType == "array")
        {
            string? itemType = null;
            if (parameter.SchemaElement is { } arraySchema &&
                arraySchema.TryGetProperty("items", out var items) &&
                items.ValueKind == JsonValueKind.Object)
            {
                itemType = GetSchemaType(ResolveSchemaRef(items, parameter.ComponentsSchemas));
            }

            var elements = new JsonArray();
            foreach (var element in SplitArrayValues(parameter, rawValues))
            {
                elements.Add(CoercePrimitive(element, itemType));
            }
            return elements;
        }

        if (schemaType == "object" && !parameter.Explode)
        {
            // form + explode:false → comma-separated key,value pairs
            // e.g. "R,100,G,200,B,150" → {"R":100,"G":200,"B":150}
            var raw = rawValues.Count > 0 ? rawValues[0] : string.Empty;
            var parts = raw.Split(',');
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i + 1 < parts.Length; i += 2)
            {
                properties[parts[i]] = parts[i + 1];
            }
            return BuildObjectNode(properties, parameter.SchemaElement, parameter.ComponentsSchemas);
        }

        var single = rawValues.Count > 0 ? rawValues[0] : string.Empty;
        return CoercePrimitive(single, schemaType);
    }

    private static JsonObject BuildObjectNode(
        Dictionary<string, string> properties,
        JsonElement? schemaElement,
        JsonElement? componentsSchemas)
    {
        var propertySchemas = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (schemaElement is { } s &&
            s.TryGetProperty("properties", out var props) &&
            props.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in props.EnumerateObject())
            {
                propertySchemas[prop.Name] = GetSchemaType(ResolveSchemaRef(prop.Value, componentsSchemas));
            }
        }

        var obj = new JsonObject();
        foreach (var (key, value) in properties)
        {
            propertySchemas.TryGetValue(key, out var propType);
            obj[key] = CoercePrimitive(value, propType);
        }
        return obj;
    }

    private static IEnumerable<string> SplitArrayValues(ParameterInfo parameter, List<string> rawValues)
    {
        if (rawValues.Count > 1)
        {
            foreach (var value in rawValues)
            {
                yield return value;
            }
            yield break;
        }

        var raw = rawValues[0];

        if (raw.Length == 0)
        {
            yield break;
        }

        var delimiter = parameter.Style switch
        {
            "spaceDelimited" => ' ',
            "pipeDelimited" => '|',
            _ => ',',
        };

        foreach (var part in raw.Split(delimiter))
        {
            yield return part;
        }
    }

    private static string? GetSchemaType(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!schema.TryGetProperty("type", out var type))
        {
            return null;
        }

        if (type.ValueKind == JsonValueKind.String)
        {
            return type.GetString();
        }

        if (type.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in type.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (value != null && value != "null")
                    {
                        return value;
                    }
                }
            }
        }

        return null;
    }

    private static JsonNode? CoercePrimitive(string raw, string? type)
    {
        switch (type)
        {
            case "integer":
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                {
                    return JsonValue.Create(integer);
                }
                if (ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsignedInteger))
                {
                    return JsonValue.Create(unsignedInteger);
                }
                return JsonValue.Create(raw);

            case "number":
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    return JsonValue.Create(number);
                }
                return JsonValue.Create(raw);

            case "boolean":
                if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonValue.Create(true);
                }
                if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonValue.Create(false);
                }
                return JsonValue.Create(raw);

            default:
                return JsonValue.Create(raw);
        }
    }

    private static List<string> CollectErrorMessages(EvaluationResults evaluation)
    {
        var raw = EvaluationErrorFilter.Collect(evaluation);

        var messages = raw.ConvertAll(e => e.Message);

        if (messages.Count == 0)
        {
            messages.Add("Value does not match the schema");
        }

        return messages;
    }

    private static Dictionary<string, string> ExtractPathValues(string template, PathString path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var templateSegments = template.Trim('/').Split('/');
        var pathSegments = (path.Value ?? string.Empty).Trim('/').Split('/');

        if (templateSegments.Length != pathSegments.Length)
        {
            return result;
        }

        for (var i = 0; i < templateSegments.Length; i++)
        {
            var segment = templateSegments[i];
            if (segment.Length >= 2 && segment[0] == '{' && segment[^1] == '}')
            {
                var name = segment[1..^1];
                result[name] = Uri.UnescapeDataString(pathSegments[i]);
            }
        }

        return result;
    }

    private static List<JsonElement> CollectParameters(
        JsonElement container,
        Dictionary<string, JsonElement>? componentsParameters)
    {
        var result = new List<JsonElement>();

        if (!container.TryGetProperty("parameters", out var parameters) ||
            parameters.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var parameter in parameters.EnumerateArray())
        {
            if (parameter.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var resolved = ResolveParameterRef(parameter, componentsParameters);
            if (resolved.HasValue)
            {
                result.Add(resolved.Value);
            }
        }

        return result;
    }

    private static JsonElement? ResolveParameterRef(
        JsonElement element,
        Dictionary<string, JsonElement>? componentsParameters)
    {
        if (!element.TryGetProperty("$ref", out var refValue) ||
            refValue.ValueKind != JsonValueKind.String)
        {
            return element;
        }

        var refString = refValue.GetString();
        if (refString is null ||
            !refString.StartsWith(ComponentsParametersPrefix, StringComparison.Ordinal) ||
            componentsParameters is null)
        {
            return null;
        }

        var name = refString[ComponentsParametersPrefix.Length..];
        return componentsParameters.TryGetValue(name, out var target) ? target : null;
    }

    private static List<JsonElement> MergeParameters(
        List<JsonElement> pathLevel,
        List<JsonElement> operationLevel)
    {
        var merged = new Dictionary<(string Name, string In), JsonElement>();

        foreach (var parameter in pathLevel)
        {
            if (TryGetNameIn(parameter, out var key))
            {
                merged[key] = parameter;
            }
        }

        foreach (var parameter in operationLevel)
        {
            if (TryGetNameIn(parameter, out var key))
            {
                merged[key] = parameter;
            }
        }

        return merged.Values.ToList();
    }

    private static bool TryGetNameIn(JsonElement element, out (string Name, string In) key)
    {
        key = default;

        if (!element.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        if (!element.TryGetProperty("in", out var @in) || @in.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        key = (name.GetString()!, @in.GetString()!);
        return true;
    }

    private static bool TryBuildParameterInfo(
        JsonElement parameter,
        JsonElement? componentsSchemas,
        out ParameterInfo info)
    {
        info = default!;

        if (!TryGetNameIn(parameter, out var key))
        {
            return false;
        }

        var required = parameter.TryGetProperty("required", out var requiredValue) &&
                       requiredValue.ValueKind == JsonValueKind.True;

        var allowEmpty = parameter.TryGetProperty("allowEmptyValue", out var allowEmptyValue) &&
                         allowEmptyValue.ValueKind == JsonValueKind.True;

        var style = GetString(parameter, "style") ?? DefaultStyle(key.In);
        var explode = GetBool(parameter, "explode") ?? DefaultExplode(style);

        JsonSchema? compiledSchema = null;
        JsonElement? schemaElement = null;
        var contentEncoded = false;

        if (parameter.TryGetProperty("schema", out var schemaEl) && schemaEl.ValueKind == JsonValueKind.Object)
        {
            schemaElement = ResolveSchemaRef(schemaEl, componentsSchemas);
            var built = OpenApiSchemaBuilder.Build(schemaEl, componentsSchemas);
            compiledSchema = JsonSchema.FromText(built.ToJsonString());
        }
        else if (TryGetContentJsonSchema(parameter, out var contentSchemaEl))
        {
            schemaElement = ResolveSchemaRef(contentSchemaEl, componentsSchemas);
            var built = OpenApiSchemaBuilder.Build(contentSchemaEl, componentsSchemas);
            compiledSchema = JsonSchema.FromText(built.ToJsonString());
            contentEncoded = true;
        }

        info = new ParameterInfo(key.Name, key.In, required, allowEmpty, style, explode, compiledSchema, schemaElement, componentsSchemas, contentEncoded);
        return true;
    }

    // Walks #/components/schemas/* $refs at the top of a schema node so that
    // coercion helpers (which look at the raw JsonElement, not the compiled
    // JsonSchema) can see the referenced `type`.  The compiled schema already
    // resolves refs for validation — this only affects value coercion.
    private static JsonElement ResolveSchemaRef(JsonElement schema, JsonElement? componentsSchemas)
    {
        var current = schema;

        for (var depth = 0; depth < 32; depth++)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty("$ref", out var refEl) ||
                refEl.ValueKind != JsonValueKind.String)
            {
                return current;
            }

            var refString = refEl.GetString();
            if (refString is null ||
                !refString.StartsWith(ComponentsSchemasPrefix, StringComparison.Ordinal) ||
                componentsSchemas is not { ValueKind: JsonValueKind.Object } schemas)
            {
                return current;
            }

            var name = refString[ComponentsSchemasPrefix.Length..];
            if (!schemas.TryGetProperty(name, out var target) ||
                target.ValueKind != JsonValueKind.Object)
            {
                return current;
            }

            current = target;
        }

        return current;
    }

    private static bool TryGetContentJsonSchema(JsonElement parameter, out JsonElement schema)
    {
        schema = default;

        if (!parameter.TryGetProperty("content", out var content) ||
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

    private static string DefaultStyle(string @in) => @in switch
    {
        "query" or "cookie" => "form",
        _ => "simple",
    };

    private static bool DefaultExplode(string style) => style == "form";

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? GetBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
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

    private static Dictionary<string, JsonElement>? TryGetComponentsParameters(JsonElement root)
    {
        if (!root.TryGetProperty("components", out var components) ||
            components.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!components.TryGetProperty("parameters", out var parameters) ||
            parameters.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var entry in parameters.EnumerateObject())
        {
            dict[entry.Name] = entry.Value;
        }
        return dict;
    }

    private sealed record OperationParameters(
        string Method,
        RoutePatternMatcher Matcher,
        string Template,
        List<ParameterInfo> Parameters);

    private sealed record ParameterInfo(
        string Name,
        string In,
        bool Required,
        bool AllowEmptyValue,
        string Style,
        bool Explode,
        JsonSchema? Schema,
        JsonElement? SchemaElement,
        JsonElement? ComponentsSchemas,
        bool ContentEncoded = false);
}
