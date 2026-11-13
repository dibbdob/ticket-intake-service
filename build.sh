#!/usr/bin/env bash
# The whole gate. Tests run the service in memory in scripted mode: no model, no key, no containers.
set -euo pipefail
cd "$(dirname "$0")"

echo "== build (warnings are errors)"
dotnet build Ticketdesk.Intake.slnx --nologo -v quiet -clp:ErrorsOnly

echo "== tests"
dotnet test --project tests/Ticketdesk.Intake.Tests --no-build 2>&1 | tail -n 6

echo "== build passed"
