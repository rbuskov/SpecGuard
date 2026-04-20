namespace SpecGuard.MuseumApi.Models;

public class SpecialEventResponse
{
    public required Guid EventId { get; init; }
    public required string Name { get; init; }
    public required string Location { get; init; }
    public required string EventDescription { get; init; }
    public required List<DateOnly> Dates { get; init; }
    public required decimal Price { get; init; }
}
