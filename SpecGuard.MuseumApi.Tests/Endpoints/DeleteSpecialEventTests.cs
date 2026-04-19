using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static SpecGuard.MuseumApi.Tests.ResponseAssertions;

namespace SpecGuard.MuseumApi.Tests.Endpoints;

public class DeleteSpecialEventTests(MuseumApiFactory factory)
    : IClassFixture<MuseumApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Delete_existing_event_returns_204()
    {
        var id = await CreateEvent("ToDelete");

        var response = await client.DeleteAsync($"/special-events/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_unknown_id_returns_404()
    {
        var response = await client.DeleteAsync("/special-events/00000000-0000-0000-0000-000000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_uuid_is_rejected()
    {
        var response = await client.DeleteAsync("/special-events/not-a-uuid");

        await AssertSchemaError(response, "eventId");
    }

    private async Task<string> CreateEvent(string name)
    {
        var response = await client.PostAsJsonAsync("/special-events", new
        {
            name,
            location = "Test Room",
            eventDescription = "A test event.",
            dates = new[] { "2024-01-01" },
            price = 10,
        });
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("eventId").GetString()!;
    }
}
