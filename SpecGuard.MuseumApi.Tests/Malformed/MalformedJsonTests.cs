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
}
