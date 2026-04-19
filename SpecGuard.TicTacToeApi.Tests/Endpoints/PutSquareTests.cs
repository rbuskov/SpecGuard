using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using static SpecGuard.TicTacToeApi.Tests.ResponseAssertions;

namespace SpecGuard.TicTacToeApi.Tests.Endpoints;

public class PutSquareTests(TicTacToeApiFactory factory)
    : IClassFixture<TicTacToeApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    private static StringContent JsonContent(string body) =>
        new(body, Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json").MediaType!);

    [Fact]
    public async Task Valid_mark_returns_200_with_status()
    {
        var response = await client.PutAsync("/board/2/2", JsonContent("\"X\""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("winner", out _));
        Assert.Equal(3, root.GetProperty("board").GetArrayLength());
    }

    [Fact]
    public async Task Invalid_mark_value_returns_422()
    {
        var response = await client.PutAsync("/board/1/1", JsonContent("\"Z\""));

        await AssertSchemaError(response);
    }

    [Fact]
    public async Task Non_string_body_returns_422()
    {
        var response = await client.PutAsync("/board/1/1", JsonContent("123"));

        await AssertSchemaError(response);
    }

    [Fact]
    public async Task Row_out_of_range_returns_422()
    {
        var response = await client.PutAsync("/board/4/1", JsonContent("\"X\""));

        await AssertSchemaError(response, "row");
    }
}
