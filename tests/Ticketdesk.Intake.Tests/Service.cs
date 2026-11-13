namespace Ticketdesk.Intake.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

/// <summary>A clock that says what the test tells it to.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>The service, in memory, in scripted mode, with a fixed clock: the real HTTP pipeline and no model.</summary>
public sealed class Service : WebApplicationFactory<Program>
{
    // Wednesday 2026-11-11 10:00 UTC.
    public static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Extraction:Mode", "scripted");
        builder.ConfigureServices(s => s.AddSingleton<TimeProvider>(new FixedClock(Now)));
    }
}
