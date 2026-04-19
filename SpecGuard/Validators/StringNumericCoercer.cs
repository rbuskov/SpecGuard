using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SpecGuard.Validators.ValidationResults;

namespace SpecGuard.Validators;

/// <summary>
/// Walks a JSON body against its OpenAPI schema and rewrites string-encoded
/// numeric values into actual JSON numbers at positions whose schema admits
/// <c>integer</c> or <c>number</c>. Lets numeric keywords (<c>minimum</c>,
/// <c>maximum</c>, <c>multipleOf</c>, format range) activate during schema
/// evaluation without losing the string-shape gate advertised by the spec.
/// When a number-shaped string is too large for any .NET numeric type, a
/// validation error is emitted so the value cannot bypass range checks.
/// </summary>
internal static class StringNumericCoercer
{
    private const string ComponentsRefPrefix = "#/components/schemas/";

    private static readonly Regex NumericShape = new(
        @"^[-+]?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][-+]?\d+)?$",
        RegexOptions.Compiled);

    public static JsonNode? Coerce(
        JsonNode? instance,
        JsonElement schema,
        JsonElement? componentsSchemas,
        List<ValidationErrorResult.ValidationError> errors)
        => Walk(instance, schema, componentsSchemas, "", errors, []);

    private static JsonNode? Walk(
        JsonNode? instance,
        JsonElement schema,
        JsonElement? componentsSchemas,
        string path,
        List<ValidationErrorResult.ValidationError> errors,
        HashSet<string> visitedRefs)
    {
        if (instance is null)
        {
            return null;
        }

        var resolved = ResolveSchemaRef(schema, componentsSchemas, visitedRefs);
        if (resolved.ValueKind != JsonValueKind.Object)
        {
            return instance;
        }

        // Nullable composition: `oneOf: [ { type: null }, <branch> ]` — descend
        // into the non-null branch so the inner numeric type is seen.
        if (TryUnwrapNullable(resolved) is { } inner)
        {
            resolved = ResolveSchemaRef(inner, componentsSchemas, visitedRefs);
            if (resolved.ValueKind != JsonValueKind.Object)
            {
                return instance;
            }
        }

        if (instance is JsonValue value)
        {
            return TryCoerceStringValue(value, resolved, path, errors) ?? instance;
        }

        if (instance is JsonObject obj)
        {
            WalkObject(obj, resolved, componentsSchemas, path, errors, visitedRefs);
            return obj;
        }

        if (instance is JsonArray array)
        {
            WalkArray(array, resolved, componentsSchemas, path, errors, visitedRefs);
            return array;
        }

        return instance;
    }

    private static void WalkObject(
        JsonObject obj,
        JsonElement schema,
        JsonElement? componentsSchemas,
        string path,
        List<ValidationErrorResult.ValidationError> errors,
        HashSet<string> visitedRefs)
    {
        if (schema.TryGetProperty("properties", out var properties) &&
            properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                if (!obj.ContainsKey(property.Name))
                {
                    continue;
                }

                var existing = obj[property.Name];
                var childPath = path + "/" + EscapePointer(property.Name);
                var coerced = Walk(existing, property.Value, componentsSchemas, childPath, errors, visitedRefs);
                if (!ReferenceEquals(existing, coerced))
                {
                    obj[property.Name] = coerced;
                }
            }
        }

        if (schema.TryGetProperty("allOf", out var allOf) && allOf.ValueKind == JsonValueKind.Array)
        {
            foreach (var branch in allOf.EnumerateArray())
            {
                WalkCompositionBranch(obj, branch, componentsSchemas, path, errors, visitedRefs);
            }
        }
    }

    private static void WalkArray(
        JsonArray array,
        JsonElement schema,
        JsonElement? componentsSchemas,
        string path,
        List<ValidationErrorResult.ValidationError> errors,
        HashSet<string> visitedRefs)
    {
        if (!schema.TryGetProperty("items", out var items))
        {
            return;
        }

        for (var i = 0; i < array.Count; i++)
        {
            var element = array[i];
            var childPath = path + "/" + i.ToString(CultureInfo.InvariantCulture);
            var coerced = Walk(element, items, componentsSchemas, childPath, errors, visitedRefs);
            if (!ReferenceEquals(element, coerced))
            {
                array[i] = coerced;
            }
        }
    }

    private static void WalkCompositionBranch(
        JsonNode branchInstance,
        JsonElement branchSchema,
        JsonElement? componentsSchemas,
        string path,
        List<ValidationErrorResult.ValidationError> errors,
        HashSet<string> visitedRefs)
    {
        var resolved = ResolveSchemaRef(branchSchema, componentsSchemas, visitedRefs);
        if (resolved.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (branchInstance is JsonObject obj)
        {
            WalkObject(obj, resolved, componentsSchemas, path, errors, visitedRefs);
        }
        else if (branchInstance is JsonArray array)
        {
            WalkArray(array, resolved, componentsSchemas, path, errors, visitedRefs);
        }
    }

    private static JsonValue? TryCoerceStringValue(
        JsonValue value,
        JsonElement schema,
        string path,
        List<ValidationErrorResult.ValidationError> errors)
    {
        if (!value.TryGetValue<string>(out var raw))
        {
            return null;
        }

        var allowsInteger = SchemaAllowsType(schema, "integer");
        var allowsNumber = SchemaAllowsType(schema, "number");
        if (!allowsInteger && !allowsNumber)
        {
            return null;
        }

        if (allowsInteger)
        {
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            {
                return JsonValue.Create(l);
            }
            if (ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u))
            {
                return JsonValue.Create(u);
            }
        }

        if (allowsNumber)
        {
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) &&
                !double.IsInfinity(d) &&
                !double.IsNaN(d))
            {
                return JsonValue.Create(d);
            }
        }

        // Parsing failed. When the string is number-shaped — i.e. the schema's
        // `pattern` would match it, or in the pattern's absence it looks numeric
        // — surface an explicit error. Otherwise the string would satisfy the
        // `type: ["string", ...]` union and the `pattern` check, while
        // `minimum`/`maximum` silently skip it because they do not apply to
        // string instances.
        if (LooksNumericForSchema(raw, schema))
        {
            var targetType = allowsNumber ? "number" : "integer";
            errors.Add(new ValidationErrorResult.ValidationError(
                $"Value '{raw}' is outside the representable range for {targetType}",
                "body",
                path));
        }

        return null;
    }

    private static bool LooksNumericForSchema(string raw, JsonElement schema)
    {
        if (schema.TryGetProperty("pattern", out var patternEl) &&
            patternEl.ValueKind == JsonValueKind.String &&
            patternEl.GetString() is { } pattern)
        {
            try
            {
                return Regex.IsMatch(raw, pattern);
            }
            catch (ArgumentException)
            {
                return NumericShape.IsMatch(raw);
            }
        }

        return NumericShape.IsMatch(raw);
    }

    private static bool SchemaAllowsType(JsonElement schema, string typeName)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            return false;
        }

        if (type.ValueKind == JsonValueKind.String)
        {
            return type.GetString() == typeName;
        }

        if (type.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in type.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() == typeName)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static JsonElement? TryUnwrapNullable(JsonElement schema)
    {
        if (!schema.TryGetProperty("oneOf", out var oneOf) ||
            oneOf.ValueKind != JsonValueKind.Array ||
            oneOf.GetArrayLength() != 2)
        {
            return null;
        }

        JsonElement? candidate = null;

        foreach (var branch in oneOf.EnumerateArray())
        {
            if (branch.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (IsNullType(branch))
            {
                continue;
            }

            if (candidate is not null)
            {
                return null;
            }

            candidate = branch;
        }

        return candidate;
    }

    private static bool IsNullType(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return type.GetString() == "null";
    }

    private static JsonElement ResolveSchemaRef(
        JsonElement schema,
        JsonElement? componentsSchemas,
        HashSet<string> visitedRefs)
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
                !refString.StartsWith(ComponentsRefPrefix, StringComparison.Ordinal) ||
                componentsSchemas is not { ValueKind: JsonValueKind.Object } schemas)
            {
                return current;
            }

            var name = refString[ComponentsRefPrefix.Length..];
            if (!visitedRefs.Add(name) ||
                !schemas.TryGetProperty(name, out var target) ||
                target.ValueKind != JsonValueKind.Object)
            {
                return current;
            }

            current = target;
        }

        return current;
    }

    private static string EscapePointer(string segment)
        => segment.Replace("~", "~0").Replace("/", "~1");
}
