using System.Net;
using System.Text.Json;
using static SpecGuard.MuseumApi.Tests.ResponseAssertions;

namespace SpecGuard.MuseumApi.Tests.Endpoints;

public class GetMuseumHoursTests(MuseumApiFactory factory)
    : IClassFixture<MuseumApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Default_request_returns_200_with_hours()
    {
        var response = await client.GetAsync("/museum-hours");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.True(root.GetArrayLength() > 0);

        var first = root[0];
        Assert.False(string.IsNullOrEmpty(first.GetProperty("date").GetString()));
        Assert.False(string.IsNullOrEmpty(first.GetProperty("timeOpen").GetString()));
        Assert.False(string.IsNullOrEmpty(first.GetProperty("timeClose").GetString()));
    }

    [Fact]
    public async Task Limit_controls_page_size()
    {
        var response = await client.GetAsync("/museum-hours?limit=3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Non_numeric_limit_is_rejected()
    {
        var response = await client.GetAsync("/museum-hours?limit=abc");

        await AssertSchemaError(response, "limit");
    }

    [Fact]
    public async Task Non_numeric_page_is_rejected()
    {
        var response = await client.GetAsync("/museum-hours?page=abc");

        await AssertSchemaError(response, "page");
    }

    [Fact]
    public async Task Invalid_startDate_format_is_rejected()
    {
        var response = await client.GetAsync("/museum-hours?startDate=not-a-date");

        await AssertSchemaError(response, "startDate");
    }
}
