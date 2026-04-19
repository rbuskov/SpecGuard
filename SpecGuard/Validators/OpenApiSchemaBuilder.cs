using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpecGuard.Validators;

internal static class OpenApiSchemaBuilder
{
    private const string Draft202012 = "https://json-schema.org/draft/2020-12/schema";
    private const string ComponentsRefPrefix = "#/components/schemas/";
    private const string DefsRefPrefix = "#/$defs/";

    private static readonly HashSet<string> StripKeys = new(StringComparer.Ordinal)
    {
        "discriminator", "example", "xml", "externalDocs", "deprecated",
        "readOnly", "writeOnly",
    };

    private static readonly HashSet<string> OpenApiFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "binary", "byte", "password",
    };

    public static JsonObject Build(JsonElement operationSchema, JsonElement? componentsSchemas,
        bool rejectAdditionalProperties = false)
    {
        var transformedBody = Transform(JsonNode.Parse(operationSchema.GetRawText()), rejectAdditionalProperties);

        var root = transformedBody as JsonObject ?? new JsonObject { ["allOf"] = new JsonArray(transformedBody) };

        root["$schema"] = Draft202012;
        root["$defs"] = BuildDefs(componentsSchemas, rejectAdditionalProperties);

        return root;
    }

    private static JsonObject BuildDefs(JsonElement? componentsSchemas, bool rejectAdditionalProperties)
    {
        var defs = new JsonObject();

        if (componentsSchemas is not { ValueKind: JsonValueKind.Object } schemas)
        {
            return defs;
        }

        foreach (var entry in schemas.EnumerateObject())
        {
            defs[entry.Name] = Transform(JsonNode.Parse(entry.Value.GetRawText()), rejectAdditionalProperties);
        }

        return defs;
    }

    private static JsonNode? Transform(JsonNode? node, bool rejectAdditionalProperties) => node switch
    {
        JsonObject obj => TransformObject(obj, rejectAdditionalProperties),
        JsonArray arr => TransformArray(arr, rejectAdditionalProperties),
        _ => node?.DeepClone(),
    };

    private static JsonArray TransformArray(JsonArray array, bool rejectAdditionalProperties)
    {
        var result = new JsonArray();
        foreach (var item in array)
        {
            result.Add(Transform(item, rejectAdditionalProperties));
        }
        return result;
    }

    private static JsonObject TransformObject(JsonObject obj, bool rejectAdditionalProperties)
    {
        var result = new JsonObject();

        // Collect readOnly property names from the source BEFORE transformation
        // strips the readOnly keyword. StripReadOnlyFromRequired needs these.
        var readOnlyNames = CollectReadOnlyNames(obj);

        var discriminatorChain = TryBuildDiscriminatorChain(obj);
        (JsonNode Min, JsonNode Max)? numericRange = null;

        foreach (var property in obj)
        {
            var key = property.Key;
            var value = property.Value;

            if (StripKeys.Contains(key))
            {
                continue;
            }

            if (key == "format" &&
                value is JsonValue formatValue &&
                formatValue.TryGetValue<string>(out var formatString))
            {
                var range = GetNumericRange(formatString);
                if (range is not null)
                {
                    numericRange = range;
                    continue;
                }

                if (OpenApiFormats.Contains(formatString))
                {
                    continue;
                }
            }

            if (key == "$ref" &&
                value is JsonValue refValue &&
                refValue.TryGetValue<string>(out var refString) &&
                refString.StartsWith(ComponentsRefPrefix, StringComparison.Ordinal))
            {
                result[key] = DefsRefPrefix + refString[ComponentsRefPrefix.Length..];
                continue;
            }

            result[key] = Transform(value, rejectAdditionalProperties);
        }

        if (rejectAdditionalProperties &&
            result.ContainsKey("properties") &&
            !result.ContainsKey("additionalProperties"))
        {
            result["additionalProperties"] = false;
        }

        if (numericRange is var (min, max))
        {
            if (!result.ContainsKey("minimum")) result["minimum"] = min;
            if (!result.ContainsKey("maximum")) result["maximum"] = max;
        }

        StripReadOnlyFromRequired(result, readOnlyNames);
        CollapseNullableComposition(result);

        if (discriminatorChain is not null)
        {
            // The discriminator chain (if/then/else) already dispatches to the
            // same $ref targets that oneOf/anyOf lists.  Keeping both produces
            // duplicate, confusing errors when validation fails.  Strip the
            // composition keyword so only the discriminator chain validates.
            result.Remove("oneOf");
            result.Remove("anyOf");

            if (result["allOf"] is JsonArray existingAllOf)
            {
                existingAllOf.Add(discriminatorChain);
            }
            else
            {
                result["allOf"] = new JsonArray(discriminatorChain);
            }
        }

        return result;
    }

    private static HashSet<string> CollectReadOnlyNames(JsonObject source)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (source["properties"] is not JsonObject properties)
        {
            return names;
        }

        foreach (var property in properties)
        {
            if (property.Value is JsonObject propertySchema &&
                propertySchema["readOnly"] is JsonValue readOnlyValue &&
                readOnlyValue.TryGetValue<bool>(out var isReadOnly) &&
                isReadOnly)
            {
                names.Add(property.Key);
            }
        }

        return names;
    }

    private static void StripReadOnlyFromRequired(JsonObject schema, HashSet<string> readOnlyNames)
    {
        // OpenAPI `readOnly: true` properties are server-assigned, so on the
        // request side they must not be enforced as required.
        if (readOnlyNames.Count == 0 ||
            schema["required"] is not JsonArray required)
        {
            return;
        }

        var filtered = new JsonArray();
        foreach (var item in required)
        {
            if (item is JsonValue value &&
                value.TryGetValue<string>(out var name) &&
                readOnlyNames.Contains(name))
            {
                continue;
            }
            filtered.Add(item?.DeepClone());
        }

        if (filtered.Count == 0)
        {
            schema.Remove("required");
        }
        else
        {
            schema["required"] = filtered;
        }
    }

    private static void CollapseNullableComposition(JsonObject schema)
    {
        // ASP.NET OpenAPI 3.1 expresses nullable types as:
        //   oneOf: [ { type: "null" }, { $ref: "..." } ]
        //
        // When the referenced schema's enum already includes null, both branches
        // match a null value and `oneOf` fails (requires exactly one match).
        //
        // Rewrite to `anyOf` which allows multiple branches to match, correctly
        // expressing "value OR null" without the exactly-one constraint.
        if (schema["oneOf"] is not JsonArray branches || branches.Count != 2)
        {
            return;
        }

        var hasNullBranch = false;
        for (var i = 0; i < 2; i++)
        {
            if (branches[i] is JsonObject branch &&
                branch["type"] is JsonValue typeValue &&
                typeValue.TryGetValue<string>(out var typeString) &&
                typeString == "null")
            {
                hasNullBranch = true;
                break;
            }
        }

        if (!hasNullBranch)
        {
            return;
        }

        // Detach the array from oneOf and reattach under anyOf.
        schema.Remove("oneOf");
        schema["anyOf"] = branches;
    }

    private static JsonNode? TryBuildDiscriminatorChain(JsonObject obj)
    {
        if (obj["discriminator"] is not JsonObject discriminator)
        {
            return null;
        }

        if (discriminator["propertyName"] is not JsonValue propertyNameValue ||
            !propertyNameValue.TryGetValue<string>(out var propertyName))
        {
            return null;
        }

        var mapping = discriminator["mapping"] as JsonObject;

        if (mapping is null || mapping.Count == 0)
        {
            // OpenAPI allows implicit mapping: infer from the $ref names in
            // oneOf/anyOf branches.  The last segment of each $ref becomes
            // the discriminator value.
            mapping = InferDiscriminatorMapping(obj);
            if (mapping is null || mapping.Count == 0)
            {
                return null;
            }
        }

        JsonObject? head = null;
        JsonObject? tail = null;

        foreach (var entry in mapping)
        {
            if (entry.Value is not JsonValue refValue ||
                !refValue.TryGetValue<string>(out var refString))
            {
                continue;
            }

            var rewrittenRef = refString.StartsWith(ComponentsRefPrefix, StringComparison.Ordinal)
                ? DefsRefPrefix + refString[ComponentsRefPrefix.Length..]
                : refString;

            var branch = new JsonObject
            {
                ["if"] = new JsonObject
                {
                    ["required"] = new JsonArray(propertyName),
                    ["properties"] = new JsonObject
                    {
                        [propertyName] = new JsonObject { ["const"] = entry.Key },
                    },
                },
                ["then"] = new JsonObject { ["$ref"] = rewrittenRef },
            };

            if (head is null)
            {
                head = branch;
            }
            else
            {
                tail!["else"] = branch;
            }

            tail = branch;
        }

        // Reject values that don't match any discriminator mapping.
        if (tail is not null)
        {
            tail["else"] = false;
        }

        return head;
    }

    private static JsonObject? InferDiscriminatorMapping(JsonObject obj)
    {
        var branches = obj["oneOf"] as JsonArray ?? obj["anyOf"] as JsonArray;
        if (branches is null || branches.Count == 0)
        {
            return null;
        }

        var mapping = new JsonObject();

        foreach (var branch in branches)
        {
            if (branch is not JsonObject branchObj)
            {
                continue;
            }

            if (branchObj["$ref"] is not JsonValue refValue ||
                !refValue.TryGetValue<string>(out var refString))
            {
                continue;
            }

            var lastSlash = refString.LastIndexOf('/');
            if (lastSlash < 0 || lastSlash == refString.Length - 1)
            {
                continue;
            }

            var name = refString[(lastSlash + 1)..];
            mapping[name] = refString;
        }

        return mapping.Count > 0 ? mapping : null;
    }

    private static (JsonNode Min, JsonNode Max)? GetNumericRange(string format) =>
        format.ToLowerInvariant() switch
        {
            "int8" => (JsonValue.Create((long)-128), JsonValue.Create((long)127)),
            "uint8" => (JsonValue.Create((long)0), JsonValue.Create((long)255)),
            "int16" => (JsonValue.Create((long)-32768), JsonValue.Create((long)32767)),
            "uint16" => (JsonValue.Create((long)0), JsonValue.Create((long)65535)),
            "int32" => (JsonValue.Create((long)int.MinValue), JsonValue.Create((long)int.MaxValue)),
            "uint32" => (JsonValue.Create((long)0), JsonValue.Create((long)uint.MaxValue)),
            "int64" => (JsonValue.Create(long.MinValue), JsonValue.Create(long.MaxValue)),
            "uint64" => (JsonValue.Create((ulong)0), JsonValue.Create(ulong.MaxValue)),
            "float16" => (JsonValue.Create((double)-65504), JsonValue.Create((double)65504)),
            "float" => (JsonValue.Create((double)float.MinValue), JsonValue.Create((double)float.MaxValue)),
            "double" => (JsonValue.Create(double.MinValue), JsonValue.Create(double.MaxValue)),
            _ => null,
        };
}
