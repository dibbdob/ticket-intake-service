namespace Ticketdesk.Intake.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ticketdesk.Extraction;

public sealed class ProductionTests
{
    private const string Email = "Our checkout at private-shop.example has been down since 9am. Enterprise.";

    private static ExtractionFailedException Refusal() =>
        new(FailureKind.Refused, "declined", new ModelReply("", "refusal", new TokenUsage(10, 0), "claude-haiku-5-5"));

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    [Test]
    public async Task An_email_the_model_refuses_is_kept_for_a_person_not_lost()
    {
        await using var service = new Service(new StubExtractor(() => throw Refusal()));
        var client = service.CreateClient();

        var response = await client.PostAsJsonAsync("/tickets", new { email = Email });
        var body = await Json(response);
        var again = await client.GetAsync($"/tickets/{body.GetProperty("id").GetString()}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(body.GetProperty("status").GetString()).IsEqualTo("unextracted");
        await Assert.That(body.GetProperty("unextracted_reason").GetString()).IsEqualTo("Refused");
        await Assert.That(body.GetProperty("for_a_person").GetBoolean()).IsTrue();
        await Assert.That(again.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task An_email_that_arrives_while_the_model_is_unreachable_is_kept_for_a_person()
    {
        await using var service = new Service(new StubExtractor(() => throw new HttpRequestException("connection refused")));

        var response = await service.CreateClient().PostAsJsonAsync("/tickets", new { email = Email });
        var body = await Json(response);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(body.GetProperty("unextracted_reason").GetString()).IsEqualTo("ModelUnavailable");
    }

    [Test]
    [Arguments("", 400)]
    [Arguments("   ", 400)]
    public async Task An_empty_email_is_refused_as_a_problem(string email, int status)
    {
        await using var service = new Service();

        var response = await service.CreateClient().PostAsJsonAsync("/tickets", new { email });

        await Assert.That((int)response.StatusCode).IsEqualTo(status);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
    }

    [Test]
    public async Task An_email_over_the_limit_is_refused_with_its_length()
    {
        await using var service = new Service();

        var response = await service.CreateClient().PostAsJsonAsync("/tickets", new { email = new string('x', NewTicket.MaxLength + 1) });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
        await Assert.That((await Json(response)).GetProperty("title").GetString()).Contains("20001 characters");
    }

    [Test]
    public async Task A_retried_post_with_the_same_key_returns_the_same_ticket_and_makes_no_second()
    {
        await using var service = new Service();
        var client = service.CreateClient();
        async Task<HttpResponseMessage> Post(string email)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/tickets") { Content = JsonContent.Create(new { email }) };
            request.Headers.Add("Idempotency-Key", "retry-1");
            return await client.SendAsync(request);
        }

        var first = await Post(Email);
        var second = await Post(Email);
        var different = await Post("Something else entirely.");

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await Json(second)).GetProperty("id").GetString()).IsEqualTo((await Json(first)).GetProperty("id").GetString());
        await Assert.That(different.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
    }

    [Test]
    public async Task Two_posts_with_the_same_key_at_the_same_moment_make_one_ticket()
    {
        // A client that times out and retries can send its second post while the first is still with the
        // model. Both must get the one ticket, and the model must be asked once.
        var release = new TaskCompletionSource<ExtractionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var service = new Service(new StubExtractor(() =>
        {
            Interlocked.Increment(ref calls);
            return release.Task;
        }));
        var client = service.CreateClient();
        Task<HttpResponseMessage> Post(string email)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/tickets") { Content = JsonContent.Create(new { email }) };
            request.Headers.Add("Idempotency-Key", "same-moment");
            return client.SendAsync(request);
        }

        var first = Post(Email);
        var second = Post(Email);
        await Task.Delay(300);
        var different = Post("Something else entirely.");
        await Task.Delay(300);
        release.SetResult(new ExtractionResult(new TicketExtraction("outage", true, "enterprise", "api", "angry", false, "s"), 1, new TokenUsage(1, 1), "scripted"));
        var both = await Task.WhenAll(first, second);
        var other = await different;

        var ids = await Task.WhenAll(both.Select(async r => (await Json(r)).GetProperty("id").GetString()));
        await Assert.That(ids[0]).IsEqualTo(ids[1]);
        await Assert.That(both.Select(r => r.StatusCode).Order()).IsEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Created });
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(other.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
    }

    [Test]
    public async Task An_email_whose_model_call_times_out_is_kept_for_a_person()
    {
        // HttpClient reports its own timeout as a cancellation, not as a TimeoutException.
        await using var service = new Service(new StubExtractor(() =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.")));

        var response = await service.CreateClient().PostAsJsonAsync("/tickets", new { email = Email });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That((await Json(response)).GetProperty("unextracted_reason").GetString()).IsEqualTo("ModelUnavailable");
    }

    [Test]
    public async Task Beyond_the_concurrency_limit_and_its_queue_a_caller_is_told_to_come_back()
    {
        // One extraction at a time, a queue of four, and every extraction held until released.
        var release = new TaskCompletionSource<ExtractionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var service = new Service(new StubExtractor(() => release.Task), concurrency: 1);
        var client = service.CreateClient();

        var posts = Enumerable.Range(0, 8).Select(i => client.PostAsJsonAsync("/tickets", new { email = $"Email {i}" })).ToList();
        await Task.Delay(500);
        release.SetResult(new ExtractionResult(new TicketExtraction("bug", false, "team", "api", "neutral", false, "s"), 1, new TokenUsage(1, 1), "scripted"));
        var responses = await Task.WhenAll(posts);

        var refused = responses.Where(r => r.StatusCode == HttpStatusCode.ServiceUnavailable).ToList();
        await Assert.That(refused.Count).IsEqualTo(3);
        await Assert.That(refused.All(r => r.Headers.RetryAfter is not null)).IsTrue();
        await Assert.That(responses.Count(r => r.StatusCode == HttpStatusCode.Created)).IsEqualTo(5);
    }

    [Test]
    public async Task The_logs_carry_ids_and_outcomes_and_never_what_the_customer_wrote()
    {
        await using var service = new Service(new StubExtractor(() => throw new HttpRequestException("connection refused")));

        await service.CreateClient().PostAsJsonAsync("/tickets", new { email = Email });

        var logs = string.Join("\n", service.Logs.Lines);
        await Assert.That(logs).Contains("Stored T-").And.Contains("unextracted");
        await Assert.That(logs).DoesNotContain("private-shop").And.DoesNotContain("checkout");
    }
}
