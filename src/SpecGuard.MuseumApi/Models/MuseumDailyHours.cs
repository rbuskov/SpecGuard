namespace SpecGuard.MuseumApi.Models;

public class MuseumDailyHours
{
    public required DateOnly Date { get; init; }
    public required TimeOnly TimeOpen { get; init; }
    public required TimeOnly TimeClose { get; init; }
}
