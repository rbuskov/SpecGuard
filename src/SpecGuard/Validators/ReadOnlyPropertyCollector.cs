using System.Text.Json;

namespace SpecGuard.Validators;

/// <summary>
/// Walks an OpenAPI schema tree to collect JSON Pointer paths of properties
/// marked <c>readOnly: true</c>. Resolves <c>$ref</c> references and
/// descends into <c>allOf</c>/<c>oneOf</c>/<c>anyOf</c> compositions.
/// </summary>
internal static class ReadOnlyPropertyCollector
{
    private const string ComponentsRefPrefix = "#/components/schemas/";

    private static readonly string[] CompositionKeywords = ["allOf", "oneOf", "anyOf"];

    public static HashSet<string> Collect(JsonElement schema, JsonElement? componentsSchemas)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        Walk(schema, "", componentsSchemas, paths, []);
        return paths;
    }

    private static void Walk(
        JsonElement schema,
        string prefix,
        JsonElement? components,
        HashSet<string> paths,
        HashSet<string> visitedRefs)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        // Resolve $ref
        if (schema.TryGetProperty("$ref", out var refEl) &&
            refEl.GetString() is { } refStr &&
            refStr.StartsWith(ComponentsRefPrefix, StringComparison.Ordinal))
        {
            var name = refStr[ComponentsRefPrefix.Length..];
            if (!visitedRefs.Add(name))
            {
                return; // cycle guard
            }

            if (components is { } c &&
                c.TryGetProperty(name, out var refSchema))
            {
                Walk(refSchema, prefix, components, paths, visitedRefs);
            }

            visitedRefs.Remove(name);
            return;
        }

        // Descend into composition keywords
        foreach (var keyword in CompositionKeywords)
        {
            if (schema.TryGetProperty(keyword, out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    Walk(item, prefix, components, paths, visitedRefs);
                }
            }
        }

        // Scan properties
        if (!schema.TryGetProperty("properties", out var props) ||
            props.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var prop in props.EnumerateObject())
        {
            var propPath = prefix + "/" + prop.Name;

            if (prop.Value.TryGetProperty("readOnly", out var ro) &&
                ro.ValueKind == JsonValueKind.True)
            {
                paths.Add(propPath);
            }

            // Recurse into nested object schemas
            Walk(prop.Value, propPath, components, paths, visitedRefs);
        }
    }
}
