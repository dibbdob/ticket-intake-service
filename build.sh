#!/usr/bin/env bash
# The whole gate. Tests run the service in memory in scripted mode: no model, no key, no containers.
set -euo pipefail
cd "$(dirname "$0")"

echo "== build (warnings are errors)"
dotnet build Ticketdesk.Intake.slnx --nologo -v quiet -clp:ErrorsOnly

echo "== tests"
dotnet test --project tests/Ticketdesk.Intake.Tests --no-build 2>&1 | tail -n 6

if command -v kubectl >/dev/null && python3 -c 'import yaml' 2>/dev/null; then
  echo "== deployment manifests: rendered, and the safeguards ADR 0002 relies on are all there"
  kubectl kustomize --load-restrictor LoadRestrictionsNone deploy | python3 scripts/check-deploy.py
else
  echo "== deployment manifests: skipped, needs kubectl and Python's yaml module"
fi

echo "== build passed"
