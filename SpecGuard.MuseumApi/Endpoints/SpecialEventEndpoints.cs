using SpecGuard.MuseumApi.Models;

namespace SpecGuard.MuseumApi.Endpoints;

public static class SpecialEventEndpoints
{
    private static readonly List<SpecialEventResponse> Events =
    [
        new()
        {
            EventId = Guid.Parse("f3e0e76e-e4a8-466e-ab9c-ae36c15b8e97"),
            Name = "Sasquatch Ballet",
            Location = "Seattle... probably",
            EventDescription = "They're big, they're hairy, but they're also graceful.",
            Dates = [new DateOnly(2023, 12, 15), new DateOnly(2023, 12, 22)],
            Price = 40,
        },
        new()
        {
            EventId = Guid.Parse("dad4bce8-f5cb-4078-a211-995864315e39"),
            Name = "Mermaid Treasure Identification and Analysis",
            Location = "Room Sea-12",
            EventDescription = "Join us as we review and classify a rare collection of 20 thingamabobs.",
            Dates = [new DateOnly(2023, 9, 5), new DateOnly(2023, 9, 8)],
            Price = 30,
        },
        new()
        {
            EventId = Guid.Parse("6744a0da-4121-49cd-8479-f8cc20526495"),
            Name = "Time Traveler Tea Party",
            Location = "Temporal Tearoom",
            EventDescription = "Sip tea with important historical figures.",
            Dates = [new DateOnly(2023, 11, 18), new DateOnly(2023, 11, 25), new DateOnly(2023, 12, 2)],
            Price = 60,
        },
    ];

    public static IEndpointRouteBuilder MapSpecialEventEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/special-events").WithTags("Events");

        group.MapPost("/", (CreateSpecialEventRequest request) =>
            {
                var evt = new SpecialEventResponse
                {
                    EventId = Guid.NewGuid(),
                    Name = request.Name,
                    Location = request.Location,
                    EventDescription = request.EventDescription,
                    Dates = request.Dates,
                    Price = request.Price,
                };
                Events.Add(evt);
                return Results.Created($"/special-events/{evt.EventId}", evt);
            })
            .WithName("createSpecialEvent")
            .Produces<SpecialEventResponse>(StatusCodes.Status201Created);

        group.MapGet("/", (DateOnly? startDate, DateOnly? endDate, int? page, int? limit) =>
            {
                var pageSize = limit ?? 10;
                var pageNumber = page ?? 1;

                var items = Events
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return Results.Ok(items);
            })
            .WithName("listSpecialEvents")
            .Produces<List<SpecialEventResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{eventId:guid}", (Guid eventId) =>
                Events.FirstOrDefault(e => e.EventId == eventId) is { } evt
                    ? Results.Ok(evt)
                    : Results.NotFound())
            .WithName("getSpecialEvent")
            .Produces<SpecialEventResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{eventId:guid}", (Guid eventId, UpdateSpecialEventRequest request) =>
            {
                var evt = Events.FirstOrDefault(e => e.EventId == eventId);
                if (evt is null)
                {
                    return Results.NotFound();
                }

                var index = Events.IndexOf(evt);
                Events[index] = new SpecialEventResponse
                {
                    EventId = evt.EventId,
                    Name = request.Name ?? evt.Name,
                    Location = request.Location ?? evt.Location,
                    EventDescription = request.EventDescription ?? evt.EventDescription,
                    Dates = request.Dates ?? evt.Dates,
                    Price = request.Price ?? evt.Price,
                };

                return Results.Ok(Events[index]);
            })
            .WithName("updateSpecialEvent")
            .Produces<SpecialEventResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{eventId:guid}", (Guid eventId) =>
            {
                var evt = Events.FirstOrDefault(e => e.EventId == eventId);
                if (evt is null)
                {
                    return Results.NotFound();
                }

                Events.Remove(evt);
                return Results.NoContent();
            })
            .WithName("deleteSpecialEvent")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
