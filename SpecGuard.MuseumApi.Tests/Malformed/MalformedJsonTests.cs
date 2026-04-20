using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SpecGuard.MuseumApi.Tests.Malformed;

public class MalformedJsonTests(MuseumApiFactory factory)
    : IClassFixture<MuseumApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task MalformedJson_returns_400_problem_details_with_line_info()
    {
        var body = new StringContent("{\n  \"name\": \"Test\",\n  \"price\": ,\n}", Encoding.UTF8);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync("/special-events", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("Malformed JSON", root.GetProperty("title").GetString());
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task ValidJson_passes_through_middleware()
    {
        var response = await client.PostAsJsonAsync("/special-events", new
        {
            name = "Test Event",
            location = "Room 1",
            eventDescription = "A test event.",
            dates = new[] { "2024-01-01" },
            price = 10,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Problem_plus_json_body_is_parsed_and_validated()
    {
        var body = new StringContent("""{"name":"Acme"}""", Encoding.UTF8);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/problem+json");

        var response = await client.PostAsync("/special-events", body);

        // Body is treated as JSON via the +json suffix and validated — the
        // missing required fields produce 422.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Empty_body_with_application_json_returns_422()
    {
        var body = new StringContent("", Encoding.UTF8);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync("/special-events", body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors");
        Assert.Contains(errors.EnumerateArray(),
            e => e.GetProperty("in").GetString() == "body"
                 && e.GetProperty("message").GetString()!.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Empty_body_with_no_content_type_returns_422()
    {
        // No Content-Type at all — SpecGuard treats the operation as matched
        // (route resolves) and JsonBodyValidator fires the required-body
        // error because no parsed body or empty marker is present.
        var request = new HttpRequestMessage(HttpMethod.Post, "/special-events");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
