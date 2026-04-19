using System.Net;
using System.Text.Json;

namespace SpecGuard.TicTacToeApi.Tests.Endpoints;

public class GetBoardTests(TicTacToeApiFactory factory)
    : IClassFixture<TicTacToeApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Returns_status_with_winner_and_board()
    {
        var response = await client.GetAsync("/board");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.False(string.IsNullOrEmpty(root.GetProperty("winner").GetString()));

        var board = root.GetProperty("board");
        Assert.Equal(JsonValueKind.Array, board.ValueKind);
        Assert.Equal(3, board.GetArrayLength());

        foreach (var row in board.EnumerateArray())
        {
            Assert.Equal(3, row.GetArrayLength());
        }
    }
}
