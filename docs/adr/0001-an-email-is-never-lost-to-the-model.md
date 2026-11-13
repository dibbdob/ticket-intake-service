# 0001 An email is never lost to the model

Status: accepted

## Context

The first version stored a ticket only when extraction succeeded. A refusal, a reply still invalid after
its correction, a rate limit, an outage at the API or an expired key each became a 500, and the
customer's email went nowhere. For a support desk that is the worst failure available: the one
customer the model could not handle is the one nobody hears from.

The same version made a second ticket when a client retried a request that had in fact succeeded, let
any number of requests call the model at once, and accepted an email of any size.

## Decision

- **Extraction is how the desk gets ahead of an email, not a condition for keeping one.** When it fails
  for any reason, the email is stored `unextracted`, at normal priority with its deadline, for a person,
  with the reason (`Refused`, `Truncated`, `InvalidAfterRetry`, `ModelUnavailable`, ...). The response
  is 202 Accepted rather than 201 Created, so a caller can tell.
- **`Idempotency-Key`.** A POST that repeats a key returns the ticket the key produced, with 200. The
  same key with a different email is a 422.
- **A concurrency limit on extraction** (four at a time by default, a queue of four times that). Past
  the queue, a caller gets 503 with `Retry-After`, rather than every request waiting on a rate-limited
  API.
- **Bad input is a problem, not a crash:** an empty email is 400 and one over 20,000 characters is 413,
  as `application/problem+json`.
- **Logs carry ids, statuses and error types, never the email.** The email is in the store.

## Consequences

- Every email the service accepts is somewhere a person will see it, whatever the model did.
- Telemetry counts `ModelUnavailable` with the other failure kinds, so an outage at the API shows on the
  dashboard as a rise in unextracted tickets, not as silence.
- The in-memory store makes idempotency per process. With a database, the key becomes a unique column,
  and the check-then-insert race the in-memory version settles with `GetOrAdd` is settled by the
  constraint.
