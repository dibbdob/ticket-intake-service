namespace Ticketdesk.Intake;

using Ticketdesk.Extraction;

/// <summary>
/// The desk's rules, the same as the sibling repositories': a priority from what the customer reported,
/// and a response deadline counted in business hours (Monday to Friday, 09:00 to 17:00 UTC).
/// </summary>
public static class Rules
{
    public static readonly IReadOnlyDictionary<string, int> ResponseHours = new Dictionary<string, int>
    {
        ["urgent"] = 4, ["high"] = 8, ["normal"] = 24, ["low"] = 40,
    };

    public static string PriorityOf(TicketExtraction e) => e switch
    {
        { Outage: true, Plan: "enterprise" } => "urgent",
        { Outage: true } => "high",
        { Category: "question" or "feature_request" } => "low",
        _ => "normal",
    };

    public static DateTimeOffset DueAt(DateTimeOffset openedAt, string priority)
    {
        var remaining = TimeSpan.FromHours(ResponseHours[priority]);
        var now = NextOpening(openedAt.ToUniversalTime());
        while (remaining > TimeSpan.Zero)
        {
            var close = new DateTimeOffset(now.Year, now.Month, now.Day, 17, 0, 0, TimeSpan.Zero);
            var spend = remaining < close - now ? remaining : close - now;
            now += spend;
            remaining -= spend;
            if (remaining > TimeSpan.Zero)
            {
                now = NextOpening(now);
            }
        }

        return now;
    }

    private static bool IsWeekday(DateTimeOffset d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    private static DateTimeOffset NextOpening(DateTimeOffset d)
    {
        var hour = d.Hour + (d.Minute / 60.0);
        if (IsWeekday(d) && hour is >= 9 and < 17)
        {
            return d;
        }

        var next = new DateTimeOffset(d.Year, d.Month, d.Day, 9, 0, 0, TimeSpan.Zero);
        if (hour >= 17 || !IsWeekday(d))
        {
            next = next.AddDays(1);
        }

        while (!IsWeekday(next))
        {
            next = next.AddDays(1);
        }

        return next;
    }
}
