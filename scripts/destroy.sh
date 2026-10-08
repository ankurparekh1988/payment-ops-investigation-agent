#!/usr/bin/env bash
# Destroys the environment's platform by running the teardown pipeline, then follows it. The
# bootstrap (state and pipeline identities) is kept, so the environment can be redeployed.
#
# Usage: scripts/destroy.sh   (requires the GitHub CLI, signed in)
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=scripts/lib/run-workflow.sh
source "$repo_root/scripts/lib/run-workflow.sh"

repo="$(gh repo view --json nameWithOwner -q .nameWithOwner)"
environment="$(gh api "repos/$repo/actions/variables/TERRAFORM_SETTINGS" --jq '.value | fromjson | .TF_VAR_environment')"
read -r -p "Destroy the '$environment' platform? Type the environment name to confirm: " answer
[[ "$answer" == "$environment" ]] || { echo "Cancelled."; exit 1; }

run_workflow teardown.yml -f "confirm=$environment"
