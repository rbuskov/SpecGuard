using System.Net;
using static SpecGuard.MuseumApi.Tests.ResponseAssertions;

namespace SpecGuard.MuseumApi.Tests.Endpoints;

public class GetTicketCodeTests(MuseumApiFactory factory)
    : IClassFixture<MuseumApiFactory>
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Valid_ticketId_returns_200_png()
    {
        var response = await client.GetAsync($"/tickets/{Guid.NewGuid()}/qr");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length >= PngSignature.Length);
        Assert.Equal(PngSignature, bytes[..PngSignature.Length]);
    }

    [Fact]
    public async Task Non_guid_ticketId_is_rejected()
    {
        var response = await client.GetAsync("/tickets/not-a-guid/qr");

        await AssertSchemaError(response, "ticketId");
    }
}
