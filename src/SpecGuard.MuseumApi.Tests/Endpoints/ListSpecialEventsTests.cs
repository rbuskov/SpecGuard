using System.Net;
using System.Text.Json;
using static SpecGuard.MuseumApi.Tests.ResponseAssertions;

namespace SpecGuard.MuseumApi.Tests.Endpoints;

public class ListSpecialEventsTests(MuseumApiFactory factory)
    : IClassFixture<MuseumApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Default_request_returns_200_with_events()
    {
        var response = await client.GetAsync("/special-events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.True(root.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Non_numeric_limit_is_rejected()
    {
        var response = await client.GetAsync("/special-events?limit=abc");

        await AssertSchemaError(response, "limit");
    }

    [Fact]
    public async Task Non_numeric_page_is_rejected()
    {
        var response = await client.GetAsync("/special-events?page=abc");

        await AssertSchemaError(response, "page");
    }

    [Fact]
    public async Task Invalid_startDate_format_is_rejected()
    {
        var response = await client.GetAsync("/special-events?startDate=not-a-date");

        await AssertSchemaError(response, "startDate");
    }

    [Fact]
    public async Task Invalid_endDate_format_is_rejected()
    {
        var response = await client.GetAsync("/special-events?endDate=not-a-date");

        await AssertSchemaError(response, "endDate");
    }
}
