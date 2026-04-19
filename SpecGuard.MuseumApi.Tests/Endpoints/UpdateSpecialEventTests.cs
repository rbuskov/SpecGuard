using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static SpecGuard.MuseumApi.Tests.ResponseAssertions;

namespace SpecGuard.MuseumApi.Tests.Endpoints;

public class UpdateSpecialEventTests(MuseumApiFactory factory)
    : IClassFixture<MuseumApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Partial_update_returns_200_with_merged_event()
    {
        var response = await client.PatchAsJsonAsync(
            "/special-events/dad4bce8-f5cb-4078-a211-995864315e39",
            new { location = "On the beach.", price = 15 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("On the beach.", root.GetProperty("location").GetString());
        Assert.Equal(15, root.GetProperty("price").GetDecimal());
        Assert.Equal("Mermaid Treasure Identification and Analysis", root.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Update_unknown_id_returns_404()
    {
        var response = await client.PatchAsJsonAsync(
            "/special-events/00000000-0000-0000-0000-000000000000",
            new { location = "Nowhere" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_type_for_price_returns_422()
    {
        var response = await client.PatchAsJsonAsync(
            "/special-events/dad4bce8-f5cb-4078-a211-995864315e39",
            new { price = "free" });

        await AssertSchemaError(response, "price");
    }
}
