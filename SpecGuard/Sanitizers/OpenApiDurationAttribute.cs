using System.Text.Json.Serialization;

namespace SpecGuard.Sanitizers;

/// <summary>
/// Marks a <see cref="TimeSpan"/> property for ISO 8601 duration serialization (e.g. "PT1H30M")
/// which corresponds to the <c>format: "duration"</c> hint in OpenAPI.
/// Applies the <see cref="TimeSpanConverter"/> for JSON and signals the
/// <see cref="TimeSpanSchemaTransformer"/> to emit <c>format: "duration"</c> in OpenAPI.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class OpenApiDurationAttribute() : JsonConverterAttribute(typeof(TimeSpanConverter));
