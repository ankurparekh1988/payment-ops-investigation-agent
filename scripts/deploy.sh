#!/usr/bin/env bash
# Deploys the environment by running the CD pipeline, then follows it to completion. Nothing is
# applied from this machine: infrastructure changes happen only in the pipeline, after approval.
#
# Usage: scripts/deploy.sh   (requires the GitHub CLI, signed in)
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=scripts/lib/run-workflow.sh
source "$repo_root/scripts/lib/run-workflow.sh"

run_workflow cd.yml
