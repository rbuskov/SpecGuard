namespace SpecGuard.MuseumApi.Models;

public class BuyMuseumTicketsResponse
{
    public required string Message { get; init; }
    public string? EventName { get; init; }
    public required Guid TicketId { get; init; }
    public required TicketType TicketType { get; init; }
    public required DateOnly TicketDate { get; init; }
    public required string ConfirmationCode { get; init; }
}
