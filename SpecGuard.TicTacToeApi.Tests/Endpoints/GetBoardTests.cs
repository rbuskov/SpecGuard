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

    [Fact]
    public async Task Winner_field_is_a_member_of_published_enum()
    {
        // Read the spec's declared enum rather than hard-coding values.
        using var spec = await FetchSpec();
        var allowed = CollectWinnerEnumValues(spec);
        Assert.NotEmpty(allowed);

        var response = await client.GetAsync("/board");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var winner = body.RootElement.GetProperty("winner").GetString();

        Assert.Contains(winner, allowed);
    }

    private async Task<JsonDocument> FetchSpec()
    {
        var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static HashSet<string?> CollectWinnerEnumValues(JsonDocument spec)
    {
        var statusSchema = spec.RootElement
            .GetProperty("paths")
            .GetProperty("/board")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        // Follow the $ref on the 200 response body schema, then find "winner".
        var resolved = ResolveRef(spec, statusSchema);
        var winner = resolved.GetProperty("properties").GetProperty("winner");
        winner = ResolveRef(spec, winner);

        var values = new HashSet<string?>();
        if (winner.TryGetProperty("enum", out var enumEl))
        {
            foreach (var v in enumEl.EnumerateArray())
            {
                values.Add(v.ValueKind == JsonValueKind.String ? v.GetString() : null);
            }
        }
        return values;
    }

    private static JsonElement ResolveRef(JsonDocument spec, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var refEl)) return schema;
        var path = refEl.GetString()!;
        const string prefix = "#/components/schemas/";
        if (!path.StartsWith(prefix)) return schema;
        return spec.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(path[prefix.Length..]);
    }
}
