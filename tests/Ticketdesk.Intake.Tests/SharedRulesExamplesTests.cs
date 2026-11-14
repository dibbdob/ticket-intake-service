namespace Ticketdesk.Intake.Tests;

using System.Globalization;
using System.Text.Json;
using Ticketdesk.Extraction;

/// <summary>
/// The desk's rules against the worked examples in Fixtures/desk-rules.json, a copy of the file
/// claude-code-governed-workflow owns. If a rule is wrong here, the example names it; if the rules have
/// changed there, copy the file again.
/// </summary>
public sealed class SharedRulesExamplesTests
{
    private static readonly JsonElement Fixture = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "desk-rules.json"))).RootElement;

    public static IEnumerable<(string Case, string Category, bool Outage, string Plan, string Priority)> PriorityExamples() =>
        Fixture.GetProperty("priority").EnumerateArray().Select(e =>
            (e.GetProperty("case").GetString()!, e.GetProperty("category").GetString()!, e.GetProperty("outage").GetBoolean(),
             e.GetProperty("plan").GetString()!, e.GetProperty("priority").GetString()!));

    public static IEnumerable<(string Case, string Opened, string Priority, string Due)> DeadlineExamples() =>
        Fixture.GetProperty("deadline").EnumerateArray().Select(e =>
            (e.GetProperty("case").GetString()!, e.GetProperty("opened").GetString()!, e.GetProperty("priority").GetString()!, e.GetProperty("due").GetString()!));

    [Test]
    [MethodDataSource(nameof(PriorityExamples))]
    public async Task Priority_agrees_with_the_shared_example(string name, string category, bool outage, string plan, string priority)
    {
        var extraction = new TicketExtraction(category, outage, plan, "api", "neutral", false, name);

        await Assert.That(Rules.PriorityOf(extraction)).IsEqualTo(priority);
    }

    [Test]
    [MethodDataSource(nameof(DeadlineExamples))]
    public async Task Deadline_agrees_with_the_shared_example(string name, string opened, string priority, string due)
    {
        var actual = Rules.DueAt(DateTimeOffset.Parse(opened, CultureInfo.InvariantCulture), priority);

        await Assert.That(actual).IsEqualTo(DateTimeOffset.Parse(due, CultureInfo.InvariantCulture));
    }
}
