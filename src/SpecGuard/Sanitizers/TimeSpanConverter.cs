using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace SpecGuard.Sanitizers;

/// <summary>
/// Serializes <see cref="TimeSpan"/> as ISO 8601 duration (e.g. "P1DT2H30M15S").
/// Handles deserialization of both ISO 8601 duration and .NET's default "d.hh:mm:ss" format.
/// </summary>
internal sealed class TimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString()
                    ?? throw new JsonException("Expected a non-null string for TimeSpan.");

        // Try ISO 8601 first (XmlConvert handles the "P..." format)
        if (value.StartsWith('P') || value.StartsWith("-P"))
        {
            try
            {
                return XmlConvert.ToTimeSpan(value);
            }
            catch (FormatException ex)
            {
                throw new JsonException($"Invalid ISO 8601 duration: '{value}'.", ex);
            }
        }

        // Fall back to .NET default format for backwards compatibility
        if (TimeSpan.TryParse(value, out var result))
        {
            return result;
        }

        throw new JsonException($"Cannot parse '{value}' as a TimeSpan.");
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(ToIso8601(value));
    }

    private static string ToIso8601(TimeSpan ts)
    {
        // XmlConvert.ToString produces a valid ISO 8601 duration
        return XmlConvert.ToString(ts);
    }
}