using System.Net;
using System.Text.Json;

namespace SpecGuard.MuseumApi.Tests;

internal static class ResponseAssertions
{
    public static async Task AssertSchemaError(HttpResponseMessage response, string? expectedFieldFragment = null)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("Validation Failed", root.GetProperty("title").GetString());
        Assert.Equal(422, root.GetProperty("status").GetInt32());

        var errors = root.GetProperty("errors");
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
        Assert.True(errors.GetArrayLength() > 0);

        if (expectedFieldFragment is not null)
        {
            var matched = errors.EnumerateArray().Any(error =>
                Contains(error, "path", expectedFieldFragment) ||
                Contains(error, "message", expectedFieldFragment));

            Assert.True(matched, $"Expected an error mentioning '{expectedFieldFragment}'.");
        }

        static bool Contains(JsonElement error, string property, string fragment) =>
            error.TryGetProperty(property, out var value) &&
            value.GetString()?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true;
    }
}
