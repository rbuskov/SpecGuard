using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static SpecGuard.MuseumApi.Tests.ResponseAssertions;

namespace SpecGuard.MuseumApi.Tests.Endpoints;

public class CreateSpecialEventTests(MuseumApiFactory factory)
    : IClassFixture<MuseumApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Valid_request_returns_201_with_generated_id()
    {
        var response = await client.PostAsJsonAsync("/special-events", new
        {
            name = "Pirate Coding Workshop",
            location = "Computer Room",
            eventDescription = "Captain Blackbeard shares his love of the C...language.",
            dates = new[] { "2023-10-29", "2023-10-30" },
            price = 45,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.False(string.IsNullOrEmpty(root.GetProperty("eventId").GetString()));
        Assert.Equal("Pirate Coding Workshop", root.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Missing_name_returns_422()
    {
        var response = await client.PostAsJsonAsync("/special-events", new
        {
            location = "Computer Room",
            eventDescription = "Some event.",
            dates = new[] { "2023-10-29" },
            price = 10,
        });

        await AssertSchemaError(response, "name");
    }

    [Fact]
    public async Task Missing_location_returns_422()
    {
        var response = await client.PostAsJsonAsync("/special-events", new
        {
            name = "Test Event",
            eventDescription = "Some event.",
            dates = new[] { "2023-10-29" },
            price = 10,
        });

        await AssertSchemaError(response, "location");
    }

    [Fact]
    public async Task Empty_body_returns_422()
    {
        var response = await client.PostAsJsonAsync("/special-events", new { });

        await AssertSchemaError(response);
    }

    [Fact]
    public async Task Wrong_type_for_price_returns_422()
    {
        var response = await client.PostAsJsonAsync("/special-events", new
        {
            name = "Test Event",
            location = "Room 1",
            eventDescription = "Some event.",
            dates = new[] { "2023-10-29" },
            price = "free",
        });

        await AssertSchemaError(response, "price");
    }

    [Fact]
    public async Task Missing_eventDescription_returns_422()
    {
        var response = await client.PostAsJsonAsync("/special-events", new
        {
            name = "Test Event",
            location = "Room 1",
            dates = new[] { "2023-10-29" },
            price = 10,
        });

        await AssertSchemaError(response, "eventDescription");
    }

    [Fact]
    public async Task Missing_dates_returns_422()
    {
        var response = await client.PostAsJsonAsync("/special-events", new
        {
            name = "Test Event",
            location = "Room 1",
            eventDescription = "Some event.",
            price = 10,
        });

        await AssertSchemaError(response, "dates");
    }

    [Fact]
    public async Task Invalid_date_in_dates_array_returns_422()
    {
        var response = await client.PostAsJsonAsync("/special-events", new
        {
            name = "Test Event",
            location = "Room 1",
            eventDescription = "Some event.",
            dates = new[] { "not-a-date" },
            price = 10,
        });

        await AssertSchemaError(response, "dates");
    }
}
