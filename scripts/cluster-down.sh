#!/usr/bin/env bash
# Remove the local cluster scripts/cluster-up.sh made, and everything in it.
set -euo pipefail
kind delete cluster --name ticketdesk
