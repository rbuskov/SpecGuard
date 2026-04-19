namespace SpecGuard.MuseumApi.Models;

public class CreateSpecialEventRequest
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required string EventDescription { get; set; }
    public required List<DateOnly> Dates { get; set; }
    public required decimal Price { get; set; }
}
