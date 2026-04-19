using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static SpecGuard.MuseumApi.Tests.ResponseAssertions;

namespace SpecGuard.MuseumApi.Tests.Endpoints;

public class BuyMuseumTicketsTests(MuseumApiFactory factory)
    : IClassFixture<MuseumApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Valid_general_ticket_returns_201()
    {
        var response = await client.PostAsJsonAsync("/tickets", new
        {
            ticketType = "General",
            ticketDate = "2023-09-07",
            email = "visitor@example.com",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.False(string.IsNullOrEmpty(root.GetProperty("ticketId").GetString()));
        Assert.Equal("General", root.GetProperty("ticketType").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("confirmationCode").GetString()));
    }

    [Fact]
    public async Task Missing_email_returns_422()
    {
        var response = await client.PostAsJsonAsync("/tickets", new
        {
            ticketType = "general",
            ticketDate = "2023-09-07",
        });

        await AssertSchemaError(response, "email");
    }

    [Fact]
    public async Task Missing_ticketType_returns_422()
    {
        var response = await client.PostAsJsonAsync("/tickets", new
        {
            ticketDate = "2023-09-07",
            email = "visitor@example.com",
        });

        await AssertSchemaError(response, "ticketType");
    }

    [Fact]
    public async Task Empty_body_returns_422()
    {
        var response = await client.PostAsJsonAsync("/tickets", new { });

        await AssertSchemaError(response);
    }

    [Fact]
    public async Task Missing_ticketDate_returns_422()
    {
        var response = await client.PostAsJsonAsync("/tickets", new
        {
            ticketType = "general",
            email = "visitor@example.com",
        });

        await AssertSchemaError(response, "ticketDate");
    }

    [Fact]
    public async Task Invalid_ticketType_enum_returns_422()
    {
        var response = await client.PostAsJsonAsync("/tickets", new
        {
            ticketType = "banana",
            ticketDate = "2023-09-07",
            email = "visitor@example.com",
        });

        await AssertSchemaError(response, "ticketType");
    }
}
