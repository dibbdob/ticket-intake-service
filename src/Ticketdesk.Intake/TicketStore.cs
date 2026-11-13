namespace Ticketdesk.Intake;

using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Ticketdesk.Extraction;

/// <summary>A ticket as the desk stores it: what was extracted, and what the desk's rules made of it.</summary>
public sealed record StoredTicket(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("received_at")] DateTimeOffset ReceivedAt,
    [property: JsonPropertyName("extraction")] TicketExtraction Extraction,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("due_at")] DateTimeOffset DueAt,
    [property: JsonPropertyName("for_a_person")] bool ForAPerson,
    [property: JsonPropertyName("answered_by")] string AnsweredBy,
    [property: JsonPropertyName("attempts")] int Attempts);

/// <summary>In memory. A real deployment puts a database here; nothing else would change.</summary>
public sealed class TicketStore
{
    private readonly ConcurrentDictionary<string, StoredTicket> tickets = new();
    private int next = 2000;

    public StoredTicket Add(Func<string, StoredTicket> create)
    {
        var id = $"T-{Interlocked.Increment(ref this.next)}";
        var ticket = create(id);
        this.tickets[id] = ticket;
        return ticket;
    }

    public StoredTicket? Find(string id) => this.tickets.GetValueOrDefault(id.ToUpperInvariant());
}
