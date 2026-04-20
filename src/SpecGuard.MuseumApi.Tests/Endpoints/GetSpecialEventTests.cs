using System.Net;
using System.Text.Json;
using static SpecGuard.MuseumApi.Tests.ResponseAssertions;

namespace SpecGuard.MuseumApi.Tests.Endpoints;

public class GetSpecialEventTests(MuseumApiFactory factory)
    : IClassFixture<MuseumApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Valid_id_returns_200_with_event()
    {
        var response = await client.GetAsync("/special-events/f3e0e76e-e4a8-466e-ab9c-ae36c15b8e97");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("Sasquatch Ballet", root.GetProperty("name").GetString());
        Assert.Equal("Seattle... probably", root.GetProperty("location").GetString());
    }

    [Fact]
    public async Task Unknown_id_returns_404()
    {
        var response = await client.GetAsync("/special-events/00000000-0000-0000-0000-000000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_uuid_is_rejected()
    {
        var response = await client.GetAsync("/special-events/not-a-uuid");

        await AssertSchemaError(response, "eventId");
    }
}
