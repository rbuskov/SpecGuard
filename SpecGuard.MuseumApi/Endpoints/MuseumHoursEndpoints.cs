using SpecGuard.MuseumApi.Models;

namespace SpecGuard.MuseumApi.Endpoints;

public static class MuseumHoursEndpoints
{
    private static readonly List<MuseumDailyHours> Hours =
    [
        new() { Date = new DateOnly(2024, 12, 1), TimeOpen = new TimeOnly(9, 0), TimeClose = new TimeOnly(18, 0) },
        new() { Date = new DateOnly(2024, 12, 2), TimeOpen = new TimeOnly(9, 0), TimeClose = new TimeOnly(18, 0) },
        new() { Date = new DateOnly(2024, 12, 3), TimeOpen = new TimeOnly(9, 0), TimeClose = new TimeOnly(18, 0) },
        new() { Date = new DateOnly(2024, 12, 4), TimeOpen = new TimeOnly(9, 0), TimeClose = new TimeOnly(18, 0) },
        new() { Date = new DateOnly(2024, 12, 5), TimeOpen = new TimeOnly(10, 0), TimeClose = new TimeOnly(16, 0) },
        new() { Date = new DateOnly(2024, 12, 6), TimeOpen = new TimeOnly(10, 0), TimeClose = new TimeOnly(16, 0) },
        new() { Date = new DateOnly(2024, 12, 7), TimeOpen = new TimeOnly(9, 0), TimeClose = new TimeOnly(18, 0) },
        new() { Date = new DateOnly(2024, 12, 8), TimeOpen = new TimeOnly(9, 0), TimeClose = new TimeOnly(18, 0) },
        new() { Date = new DateOnly(2024, 12, 9), TimeOpen = new TimeOnly(9, 0), TimeClose = new TimeOnly(18, 0) },
        new() { Date = new DateOnly(2024, 12, 10), TimeOpen = new TimeOnly(9, 0), TimeClose = new TimeOnly(18, 0) },
    ];

    public static IEndpointRouteBuilder MapMuseumHoursEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/museum-hours").WithTags("Operations");

        group.MapGet("/", (DateOnly? startDate, int? page, int? limit) =>
            {
                var pageSize = limit ?? 10;
                var pageNumber = page ?? 1;

                var query = Hours.AsEnumerable();
                if (startDate is { } from)
                {
                    query = query.Where(h => h.Date >= from);
                }

                var items = query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return Results.Ok(items);
            })
            .WithName("getMuseumHours")
            .Produces<List<MuseumDailyHours>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }
}
