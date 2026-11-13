# 0002 Size the service against the API, and keep the key out of configuration

Status: accepted

## Context

Locally, the service runs once and the key sits in an ignored `.env`. Deployed, two things change. It
scales, and every replica calls the same API under the same rate limit, so adding replicas past a point
turns waiting into rate-limit errors. And the key has to reach the container from somewhere that is not a
file in the repository, a command line or deployment history.

The deployment should also be something a reviewer can run, not only read: a template for a cloud
subscription is checked by a linter at best, and its claims stay claims until someone pays to deploy it.

## Decision

The service deploys to **Kubernetes**, described in `deploy/` and run locally on a **kind** cluster
(Kubernetes in Docker) by `scripts/cluster-up.sh`. The same manifests describe a deployment to any cloud's
managed Kubernetes; what changes there is listed below.

- **Replicas are sized against the API.** One replica always up (a cold start delays the first email of
  the day), at most three, scaled by CPU. Each runs at most four extractions at once, so the most the
  service asks of the API at once is twelve. Both numbers are in `deploy/intake.yaml`, and the runbook says
  to raise them only against the API's rate limit.
- **The key never enters a manifest.** It reaches the container from a Kubernetes Secret, which
  `cluster-up.sh --live` creates from the ignored `.env` without passing it on a command line. In scripted
  mode there is no Secret at all, and the reference to it is optional.
- **The service has its own identity, with nothing it does not need.** Its service account mounts no
  Kubernetes API token, because the service never calls the API.
- **Contained by default.** The namespace enforces Kubernetes' "restricted" pod security standard; the
  container runs as a non-root user with a read-only filesystem and no capabilities, with CPU and memory
  requests and limits, and with startup, readiness and liveness probes.
- **Only the traffic it needs.** A network policy allows requests in, and out only to DNS, the telemetry
  stack and HTTPS for the API.
- **Telemetry goes to a stack beside it.** The same OpenTelemetry stack and dashboard as the Compose setup
  run in their own namespace.
- **The build checks the safeguards.** `scripts/check-deploy.py` reads the rendered manifests and fails if
  any of the above is missing, and was seen failing on planted breaches.

## On a cloud

| Here | On a managed cloud cluster (AKS, EKS, GKE) |
|---|---|
| A Secret created from `.env` | A secret store (Key Vault, Secrets Manager, Secret Manager) synced by its CSI driver or External Secrets, read through workload identity |
| A service account with no API token | The same, federated with a cloud identity that may read that one secret |
| The image loaded into kind | An image pushed to a registry, by digest |
| metrics-server, pinned in `deploy/cluster/` | The platform's own |
| kind's network, which does not enforce network policies | A network plugin that does, so the policy applies |
| `kubectl port-forward` | An ingress or gateway with TLS |
| The telemetry stack in the cluster | A managed observability service, fed by the same OTLP exporter |

## Consequences

- A reviewer can deploy it in a few minutes with Docker, kind and kubectl, and nothing costs money unless
  live mode is chosen.
- Rotating the key is a new Secret; a rollout restart picks it up.
- What a cloud deployment would add (registry, identity federation, an enforcing network plugin, TLS) is
  named above but not exercised here.
