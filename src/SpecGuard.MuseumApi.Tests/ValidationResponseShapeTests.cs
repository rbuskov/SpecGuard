using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SpecGuard.MuseumApi.Tests;

public class ValidationResponseShapeTests(MuseumApiFactory factory)
    : IClassFixture<MuseumApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Validation_failure_returns_problem_json_content_type()
    {
        var response = await client.PostAsJsonAsync("/special-events", new { });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Validation_failure_body_uses_camelCase_error_fields()
    {
        var response = await client.PostAsJsonAsync("/special-events", new { });

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0);

        var first = errors[0];
        Assert.True(first.TryGetProperty("message", out _));
        Assert.True(first.TryGetProperty("in", out _));
        Assert.True(first.TryGetProperty("path", out _));
    }

    [Fact]
    public async Task Validation_failure_error_in_values_are_members_of_documented_set()
    {
        // Multiple error categories at once: missing body (which 'in: body')
        // plus a bad path param (which 'in: path' would be triggered via a
        // different endpoint). Here we stitch together a request that
        // produces several 'in' values.
        var response = await client.GetAsync("/museum-hours?startDate=not-a-date");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var allowed = new HashSet<string> { "body", "path", "query", "header", "cookie" };

        foreach (var error in document.RootElement.GetProperty("errors").EnumerateArray())
        {
            Assert.Contains(error.GetProperty("in").GetString(), allowed!);
        }
    }

    [Fact]
    public async Task Validation_failure_body_includes_rfc_type_url()
    {
        var response = await client.PostAsJsonAsync("/special-events", new { });

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.21",
            document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Errors_array_serializes_as_json_array()
    {
        var response = await client.PostAsJsonAsync("/special-events", new { });

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("errors").ValueKind);
    }

    [Fact]
    public async Task Body_level_error_serializes_path_as_empty_string_not_omitted()
    {
        // A required-body violation produces { in: "body", path: "" }.
        var response = await client.PostAsync(
            "/special-events",
            new StringContent("", System.Text.Encoding.UTF8, "application/json"));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0);

        var bodyError = errors.EnumerateArray()
            .First(e => e.GetProperty("in").GetString() == "body");

        Assert.Equal(JsonValueKind.String, bodyError.GetProperty("path").ValueKind);
        Assert.Equal("", bodyError.GetProperty("path").GetString());
    }
}
