# Runbook

## Run it

```bash
docker compose up -d --build        # the service on 127.0.0.1:8080, Grafana on 127.0.0.1:3000
curl -s localhost:8080/healthz      # {"status":"ok","mode":"scripted",...}
docker compose down                 # stop; the observability data goes with the container
```

Scripted mode needs no key and costs nothing. To call the Claude API, copy `.env.example` to `.env`, set
`EXTRACTION_MODE=live` and `ANTHROPIC_API_KEY`, and run `docker compose up -d` again. Set a spend limit on
the key in the Claude Console first. `.env` is ignored by git.

Send an email:

```bash
curl -s localhost:8080/tickets -H 'Content-Type: application/json' -H 'Idempotency-Key: demo-1' \
  -d '{"email":"Every call to the API returns 503. Enterprise."}'
```

The dashboard is **Ticketdesk intake** in Grafana.

## What the responses mean

| Status | Meaning | What to do |
|---|---|---|
| 201 | Extracted, prioritised, stored | Nothing |
| 202 | Stored **unextracted**, for a person; `unextracted_reason` says why | See below if many arrive |
| 200 | A retry with an `Idempotency-Key` already used for this email | Nothing; no second ticket was made |
| 400 / 413 | Empty, or over 20,000 characters | The caller's to fix |
| 422 | An `Idempotency-Key` reused for a different email | The caller's to fix |
| 503 + `Retry-After` | Every extraction slot and queue place is taken | Retry; if sustained, see below |

## When something looks wrong

**Many `ModelUnavailable` (failures panel, 202s).** The API cannot be reached or refuses the key. Check
the service log for the error type (`AnthropicException`, `HttpRequestException`), then the API's
status and the key's spend limit. Emails are being kept for a person meanwhile; nothing is lost.

**Many `Refused`.** A safety classifier is declining emails. Read a few of the unextracted tickets
(GET `/tickets/{id}` shows the reason; the email is in the store). If they share a pattern, it belongs in
the extractor's eval set in `claude-structured-extraction`, not in a prompt tweak here.

**Many `InvalidAfterRetry`.** The model's extractions are breaking the desk's rules even after
correction. Run the extractor's eval on the current model; a model change is the usual cause.

**Sustained 503s.** Extractions are slower than emails arrive. Raise `Extraction__Concurrency` only as
far as the API rate limit allows: concurrency times replicas is what the service asks of the API at once.

**Cost rising faster than tickets.** In the tokens panel, compare `cache_read` with `input`. A cache that
never reads means the system prompt's prefix is changing between requests; the extractor's tests guard
against that, so check what changed in the vendored copy.

**Extraction time p95 jumps.** Usually the model or the API, not the service: check whether retries are
up (`attempts` on tickets) before looking at the container.

## Deploying to Kubernetes

`deploy/` describes the service on Kubernetes (ADR 0002): one replica always up and at most three, scaled
by CPU; the API key from a Secret; a restricted, read-only, non-root container with probes and limits; and
the telemetry stack beside it. `./build.sh` checks those safeguards are present in the rendered manifests.

To run it locally on a kind cluster (Docker, kind and kubectl needed; scripted mode, no cost):

```bash
scripts/cluster-up.sh                                       # create the cluster, build, load and deploy
kubectl -n ticketdesk port-forward svc/intake 8080:8080     # the service, as above, on 127.0.0.1:8080
kubectl -n observability port-forward svc/lgtm 3000:3000    # Grafana and the dashboard
kubectl -n ticketdesk get hpa                                # replicas, and the CPU they are scaling on
scripts/cluster-down.sh                                     # remove it all
```

For live extraction, put the key in `.env` and run `scripts/cluster-up.sh --live`: it becomes a Secret and
the service switches mode. Raise the replica ceiling or the per-replica concurrency only against the API's
rate limit: their product is the most the service asks of the API at once.

On a cloud's managed Kubernetes, the same manifests apply with the changes ADR 0002 lists: the image from a
registry, the key from the cloud's secret store through workload identity, and an ingress with TLS.
