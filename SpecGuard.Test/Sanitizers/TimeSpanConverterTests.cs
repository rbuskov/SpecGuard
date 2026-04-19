using System.Text.Json;

namespace SpecGuard.Test.Sanitizers;

public class TimeSpanConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new TimeSpanConverter() },
    };

    [Theory]
    [InlineData("\"PT1H30M\"", 1, 30, 0)]
    [InlineData("\"PT0S\"", 0, 0, 0)]
    [InlineData("\"P1DT2H\"", 26, 0, 0)]
    public void Reads_iso_8601_duration(string json, int hours, int minutes, int seconds)
    {
        var value = JsonSerializer.Deserialize<TimeSpan>(json, Options);

        Assert.Equal(new TimeSpan(hours, minutes, seconds), value);
    }

    [Fact]
    public void Reads_negative_iso_8601_duration()
    {
        var value = JsonSerializer.Deserialize<TimeSpan>("\"-P1D\"", Options);

        Assert.Equal(TimeSpan.FromDays(-1), value);
    }

    [Fact]
    public void Reads_dotnet_default_format_as_fallback()
    {
        var value = JsonSerializer.Deserialize<TimeSpan>("\"01:30:00\"", Options);

        Assert.Equal(new TimeSpan(1, 30, 0), value);
    }

    [Fact]
    public void Reads_dotnet_default_format_with_days()
    {
        var value = JsonSerializer.Deserialize<TimeSpan>("\"1.02:30:00\"", Options);

        Assert.Equal(new TimeSpan(1, 2, 30, 0), value);
    }

    [Fact]
    public void Throws_on_malformed_iso_8601()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<TimeSpan>("\"PT\"", Options));
    }

    [Fact]
    public void Throws_on_uninterpretable_string()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<TimeSpan>("\"banana\"", Options));
    }

    [Fact]
    public void Throws_on_null_json_value()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<TimeSpan>("null", Options));
    }

    [Fact]
    public void Writes_iso_8601_duration()
    {
        var json = JsonSerializer.Serialize(new TimeSpan(1, 30, 0), Options);

        Assert.Equal("\"PT1H30M\"", json);
    }

    [Fact]
    public void Write_then_read_round_trips_positive_value()
    {
        var original = new TimeSpan(3, 14, 15, 9);

        var roundtripped = JsonSerializer.Deserialize<TimeSpan>(
            JsonSerializer.Serialize(original, Options), Options);

        Assert.Equal(original, roundtripped);
    }

    [Fact]
    public void Write_then_read_round_trips_negative_value()
    {
        var original = TimeSpan.FromHours(-2);

        var roundtripped = JsonSerializer.Deserialize<TimeSpan>(
            JsonSerializer.Serialize(original, Options), Options);

        Assert.Equal(original, roundtripped);
    }
}
