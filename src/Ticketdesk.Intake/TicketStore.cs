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
    private readonly ConcurrentDictionary<string, KeyClaim> byIdempotencyKey = new();
    private int next = 2000;

    public StoredTicket Add(Func<string, StoredTicket> create)
    {
        var id = $"T-{Interlocked.Increment(ref this.next)}";
        var ticket = create(id);
        this.tickets[id] = ticket;
        return ticket;
    }

    public StoredTicket? Find(string id) => this.tickets.GetValueOrDefault(id.ToUpperInvariant());

    /// <summary>
    /// The ticket an Idempotency-Key stands for, made once however many requests carry the key at the same
    /// moment. The key is claimed when its first request arrives, not when the model answers, so a retry that
    /// comes while the first is still being extracted waits for that ticket instead of making a second. A key
    /// already claimed for a different email is a conflict. If making the ticket fails, the claim is released
    /// so a retry can try again.
    /// </summary>
    public async Task<KeyedTicket> ForKey(string key, string email, Func<Task<StoredTicket>> create)
    {
        var mine = new KeyClaim(email, new Lazy<Task<StoredTicket>>(create));
        var claim = this.byIdempotencyKey.GetOrAdd(key, mine);
        if (claim.Email != email)
        {
            return new KeyedTicket(null, Created: false, Conflict: true);
        }

        try
        {
            return new KeyedTicket(await claim.Ticket.Value, Created: ReferenceEquals(claim, mine), Conflict: false);
        }
        catch
        {
            this.byIdempotencyKey.TryRemove(KeyValuePair.Create(key, claim));
            throw;
        }
    }

    private sealed record KeyClaim(string Email, Lazy<Task<StoredTicket>> Ticket);
}

/// <summary>What an Idempotency-Key gave: the ticket, whether this request made it, or a conflict.</summary>
public sealed record KeyedTicket(StoredTicket? Ticket, bool Created, bool Conflict);
