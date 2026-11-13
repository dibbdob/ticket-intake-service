namespace Ticketdesk.Intake.Tests;

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ticketdesk.Extraction;

/// <summary>A clock that says what the test tells it to.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Every log line the service writes, as text.</summary>
public sealed class LogCapture : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this.Lines);

    public void Dispose()
    {
    }

    private sealed class Logger(ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : $" {exception}"));
    }
}

/// <summary>
/// The service, in memory, in scripted mode, with a fixed clock: the real HTTP pipeline and no model.
/// A test can replace the extractor, cap concurrency, and read the logs.
/// </summary>
public sealed class Service(IExtractor? extractor = null, int concurrency = 4) : WebApplicationFactory<Program>
{
    // Wednesday 2026-11-11 10:00 UTC.
    public static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    public LogCapture Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Extraction:Mode", "scripted");
        builder.UseSetting("Extraction:Concurrency", concurrency.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureLogging(l => l.AddProvider(this.Logs));
        builder.ConfigureServices(s =>
        {
            s.AddSingleton<TimeProvider>(new FixedClock(Now));
            if (extractor is not null)
            {
                s.AddSingleton(extractor);
            }
        });
    }
}

/// <summary>An extractor that does whatever the test needs: throw, or wait until released.</summary>
public sealed class StubExtractor(Func<Task<ExtractionResult>> behave) : IExtractor
{
    public Task<ExtractionResult> Extract(string email, CancellationToken cancellationToken = default) => behave();
}
