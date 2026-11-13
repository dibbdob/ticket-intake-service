namespace Ticketdesk.Intake;

using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Ticketdesk.Extraction;

/// <summary>
/// A ticket as the desk stores it. When extraction worked, what was extracted and what the desk's rules
/// made of it. When it did not, the email is still stored, unextracted, for a person, with why.
/// </summary>
public sealed record StoredTicket(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("received_at")] DateTimeOffset ReceivedAt,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("extraction")] TicketExtraction? Extraction,
    [property: JsonPropertyName("unextracted_reason")] string? UnextractedReason,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("due_at")] DateTimeOffset DueAt,
    [property: JsonPropertyName("for_a_person")] bool ForAPerson,
    [property: JsonPropertyName("answered_by")] string? AnsweredBy,
    [property: JsonPropertyName("attempts")] int Attempts)
{
    [JsonIgnore]
    public string Email { get; init; } = string.Empty;
}

/// <summary>In memory. A real deployment puts a database here; nothing else would change.</summary>
public sealed class TicketStore
{
    private readonly ConcurrentDictionary<string, StoredTicket> tickets = new();
    private readonly ConcurrentDictionary<string, (string TicketId, string Email)> byIdempotencyKey = new();
    private int next = 2000;

    public StoredTicket Add(Func<string, StoredTicket> create)
    {
        var id = $"T-{Interlocked.Increment(ref this.next)}";
        var ticket = create(id);
        this.tickets[id] = ticket;
        return ticket;
    }

    public StoredTicket? Find(string id) => this.tickets.GetValueOrDefault(id.ToUpperInvariant());

    public (string TicketId, string Email)? ForKey(string key) =>
        this.byIdempotencyKey.TryGetValue(key, out var hit) ? hit : null;

    /// <summary>Records which ticket a key produced; the first ticket stored under a key wins.</summary>
    public StoredTicket Remember(string key, StoredTicket ticket) =>
        this.byIdempotencyKey.GetOrAdd(key, (ticket.Id, ticket.Email)).TicketId is var winner && winner == ticket.Id
            ? ticket
            : this.Find(winner)!;
}
