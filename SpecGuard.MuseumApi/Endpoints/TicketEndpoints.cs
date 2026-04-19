using SpecGuard.MuseumApi.Models;

namespace SpecGuard.MuseumApi.Endpoints;

public static class TicketEndpoints
{
    private static readonly byte[] QrPlaceholderPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x00, 0x00, 0x00, 0x00, 0x3A, 0x7E, 0x9B, 0x55,
        0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54,
        0x78, 0x9C, 0x62, 0x00, 0x00, 0x00, 0x00, 0x02,
        0x00, 0x01, 0xE5, 0x27, 0xDE, 0xFC,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44,
        0xAE, 0x42, 0x60, 0x82,
    ];

    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/tickets").WithTags("Tickets");

        group.MapPost("/", (BuyMuseumTicketsRequest request) =>
            {
                var ticketId = Guid.NewGuid();
                var confirmationCode = $"ticket-{request.TicketType.ToString().ToLowerInvariant()}-{ticketId.ToString()[..6]}";

                var response = new BuyMuseumTicketsResponse
                {
                    Message = request.TicketType == TicketType.Event
                        ? "Museum special event ticket purchased"
                        : "Museum general entry ticket purchased",
                    TicketId = ticketId,
                    TicketType = request.TicketType,
                    TicketDate = request.TicketDate,
                    ConfirmationCode = confirmationCode,
                };

                return Results.Created($"/tickets/{ticketId}", response);
            })
            .WithName("buyMuseumTickets")
            .Produces<BuyMuseumTicketsResponse>(StatusCodes.Status201Created);

        group.MapGet("/{ticketId:guid}/qr", (Guid ticketId) =>
                Results.File(QrPlaceholderPng, "image/png"))
            .WithName("getTicketCode")
            .Produces<byte[]>(StatusCodes.Status200OK, "image/png")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
