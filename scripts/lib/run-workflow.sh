#!/usr/bin/env bash
# Starts a workflow on main and follows the run until it finishes, pointing out approval gates.

run_workflow() {
  local workflow="$1"
  shift

  local repo started run_id
  repo="$(gh repo view --json nameWithOwner -q .nameWithOwner)"
  started="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

  gh workflow run "$workflow" --repo "$repo" --ref main "$@"

  # The run appears a few seconds after it's requested.
  for _ in $(seq 1 30); do
    run_id="$(gh run list --repo "$repo" --workflow "$workflow" --event workflow_dispatch \
      --created ">=$started" --limit 1 --json databaseId --jq '.[0].databaseId // empty')"
    [[ -n "$run_id" ]] && break
    sleep 2
  done
  [[ -n "${run_id:-}" ]] || { echo "Couldn't find the $workflow run." >&2; return 1; }

  echo "Following $(gh run view "$run_id" --repo "$repo" --json url -q .url)"
  echo "Infrastructure changes wait for approval in the run's environment; approve them on that page."
  gh run watch "$run_id" --repo "$repo" --exit-status
}
