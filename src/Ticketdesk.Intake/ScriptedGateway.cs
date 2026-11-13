namespace Ticketdesk.Intake;

using System.Text.Json;
using Ticketdesk.Extraction;

/// <summary>
/// Not a model. A gateway that answers from keywords, so the whole service (schema, rules, retry, the
/// desk's rules, storage, telemetry) can run without an API key and without cost. Only the model's reply
/// is canned; everything that handles it is the real code. Selected with Extraction__Mode=scripted.
/// </summary>
public sealed class ScriptedGateway : IModelGateway
{
    public Task<ModelReply> Complete(ModelRequest request, CancellationToken cancellationToken)
    {
        var email = request.Turns[0].Text.ToLowerInvariant();
        bool Has(params string[] words) => words.Any(email.Contains);

        var outage = Has("down", "503", "nothing works", "cannot log in", "can't log in", "locked out");
        var category = outage ? "outage"
            : Has("invoice", "charged", "refund", "billing") ? "billing"
            : Has("password", "login", "sign in", "access") ? "account"
            : Has("would love", "could you add", "feature") ? "feature_request"
            : Has("how do i", "how to", "?") ? "question"
            : "bug";
        var plan = Has("enterprise") ? "enterprise" : Has("team plan", "team.") ? "team" : Has("starter") ? "starter" : "unknown";
        var area = Has("api", "503") ? "api" : Has("webhook", "slack", "zapier") ? "integrations" : Has("invoice", "charged") ? "billing" : Has("ios", "android", "app") ? "mobile" : "dashboard";
        var needsHuman = Has("legal", "security", "gdpr", "ignore all previous", "ignore your instructions");

        var reply = new TicketExtraction(category, outage, plan, area, outage ? "frustrated" : "neutral", needsHuman,
            "Scripted extraction: no model was called.");
        var usage = new TokenUsage(Input: email.Length / 4, Output: 60);
        return Task.FromResult(new ModelReply(JsonSerializer.Serialize(reply), "end_turn", usage, "scripted"));
    }
}
