namespace SpecGuard.MuseumApi.Models;

public class UpdateSpecialEventRequest
{
    public string? Name { get; set; }
    public string? Location { get; set; }
    public string? EventDescription { get; set; }
    public List<DateOnly>? Dates { get; set; }
    public decimal? Price { get; set; }
}
