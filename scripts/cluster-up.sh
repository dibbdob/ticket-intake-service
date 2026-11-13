#!/usr/bin/env bash
# Run the service on a local Kubernetes cluster (kind), as it would run on a cloud's managed Kubernetes
# (ADR 0002). Scripted mode by default: no key, no cost.
#
#   scripts/cluster-up.sh          create the cluster if needed, build and load the image, deploy
#   scripts/cluster-up.sh --live   the same, with the API key from .env as a Secret, and live extraction
#
# Needs Docker, kind and kubectl. kind makes the new cluster kubectl's current context, as it always does.
set -euo pipefail
cd "$(dirname "$0")/.."
cluster=ticketdesk
k() { kubectl --context "kind-${cluster}" "$@"; }

kind get clusters 2>/dev/null | grep -qx "$cluster" || kind create cluster --config deploy/kind-cluster.yaml

docker build -q -t ticketdesk-intake:local . >/dev/null
kind load docker-image ticketdesk-intake:local --name "$cluster"

k apply -f deploy/cluster/metrics-server.yaml >/dev/null
kubectl kustomize --load-restrictor LoadRestrictionsNone deploy | k apply -f - >/dev/null

if [[ "${1:-}" == "--live" ]]; then
  # The key goes from the ignored .env straight into a Secret: never into a manifest, an argument list or
  # the shell's history. On a cloud cluster this is a secret store (Key Vault, Secrets Manager) read
  # through the workload's own identity.
  key="$(sed -n 's/^ANTHROPIC_API_KEY=//p' .env 2>/dev/null | head -n1)"
  [[ -n "$key" ]] || { echo "Live mode needs ANTHROPIC_API_KEY in .env (copy .env.example)." >&2; exit 1; }
  printf '%s' "$key" | k -n ticketdesk create secret generic anthropic-api-key --from-file=api-key=/dev/stdin \
    --dry-run=client -o yaml | k apply -f - >/dev/null
  k -n ticketdesk set env deployment/intake Extraction__Mode=live >/dev/null
fi

k -n ticketdesk rollout status deployment/intake --timeout=180s
# The autoscaler reads CPU from metrics-server, which starts more slowly than the service.
k -n kube-system rollout status deployment/metrics-server --timeout=180s
k -n observability rollout status deployment/lgtm --timeout=300s
cat <<'TXT'

Running. In two other terminals:
  kubectl -n ticketdesk port-forward svc/intake 8080:8080        # the service on 127.0.0.1:8080
  kubectl -n observability port-forward svc/lgtm 3000:3000       # Grafana on 127.0.0.1:3000
Remove it all with scripts/cluster-down.sh.
TXT
