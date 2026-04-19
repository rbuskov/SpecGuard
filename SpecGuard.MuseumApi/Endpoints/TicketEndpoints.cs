using SpecGuard.MuseumApi.Models;

namespace SpecGuard.MuseumApi.Endpoints;

public static class TicketEndpoints
{
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
            .Produces<BuyMuseumTicketsResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }
}
