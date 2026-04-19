using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SpecGuard.TicTacToeApi.Tests.Malformed;

public class MalformedJsonTests(TicTacToeApiFactory factory)
    : IClassFixture<TicTacToeApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task MalformedJson_returns_400_problem_details_with_detail()
    {
        var body = new StringContent("\"X", Encoding.UTF8);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.PutAsync("/board/1/1", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("Malformed JSON", root.GetProperty("title").GetString());
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task ValidJson_passes_through_middleware()
    {
        var body = new StringContent("\"O\"", Encoding.UTF8);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.PutAsync("/board/3/3", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
