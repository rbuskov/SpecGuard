using System.ComponentModel.DataAnnotations;

namespace SpecGuard.MuseumApi.Models;

public class BuyMuseumTicketsRequest
{
    public required TicketType TicketType { get; set; }
    public Guid? EventId { get; set; }
    public required DateOnly TicketDate { get; set; }

    [EmailAddress]
    public required string Email { get; set; }

    public string? Phone { get; set; }
}
