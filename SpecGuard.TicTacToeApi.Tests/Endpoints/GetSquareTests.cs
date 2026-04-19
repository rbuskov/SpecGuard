using System.Net;
using System.Text.Json;
using static SpecGuard.TicTacToeApi.Tests.ResponseAssertions;

namespace SpecGuard.TicTacToeApi.Tests.Endpoints;

public class GetSquareTests(TicTacToeApiFactory factory)
    : IClassFixture<TicTacToeApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Valid_coordinates_return_200_with_mark()
    {
        var response = await client.GetAsync("/board/1/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.String, document.RootElement.ValueKind);
    }

    [Fact]
    public async Task Row_out_of_range_returns_422()
    {
        var response = await client.GetAsync("/board/5/1");

        await AssertSchemaError(response, "row");
    }

    [Fact]
    public async Task Column_below_minimum_returns_422()
    {
        var response = await client.GetAsync("/board/1/0");

        await AssertSchemaError(response, "column");
    }

    [Fact]
    public async Task Non_integer_coordinate_returns_422()
    {
        var response = await client.GetAsync("/board/abc/1");

        await AssertSchemaError(response, "row");
    }
}
