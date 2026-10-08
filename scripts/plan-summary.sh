#!/usr/bin/env bash
# Summarises a saved Terraform plan as resource addresses and actions only. Attribute values are
# never included, because PR comments and job summaries aren't masked like workflow logs.
#
# Usage: scripts/plan-summary.sh <deployment> <plan file> [pull request number]
#   Writes to the job summary and, when a PR number is given, creates or updates one PR comment.
set -euo pipefail

deployment="$1"
plan_file="$2"
pr_number="${3:-}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
marker="<!-- plan-summary:$deployment -->"
max_rows=60

changes="$(terraform -chdir="$repo_root/infra/deployments/$deployment" show -json "$plan_file" | jq -r '
  [.resource_changes[]? | select(.mode == "managed" and .change.actions != ["no-op"])
   | {action: (.change.actions | join(" then ")), address}]')"

count() { jq --arg action "$1" '[.[] | select(.action | contains($action))] | length' <<< "$changes"; }
total="$(jq 'length' <<< "$changes")"

{
  echo "$marker"
  echo "### Terraform plan: \`$deployment\`"
  echo
  if (( total == 0 )); then
    echo "No changes."
  else
    echo "**$(count create) to create · $(count update) to update · $(count delete) to delete**"
    echo
    echo "| Action | Resource |"
    echo "|---|---|"
    jq -r --argjson max "$max_rows" '.[:$max][] | "| \(.action) | `\(.address)` |"' <<< "$changes"
    (( total > max_rows )) && echo && echo "_…and $((total - max_rows)) more._"
  fi
  echo
  echo "<sub>Resource addresses only; values are in the workflow log, with secrets redacted.</sub>"
} > plan-summary.md

cat plan-summary.md >> "${GITHUB_STEP_SUMMARY:-/dev/null}"

if [[ -n "$pr_number" ]]; then
  repo="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required to comment on a pull request}"
  existing="$(gh api "repos/$repo/issues/$pr_number/comments" --paginate \
    --jq "[.[] | select(.body | startswith(\"$marker\"))][0].id // empty")"
  if [[ -n "$existing" ]]; then
    gh api -X PATCH "repos/$repo/issues/comments/$existing" -F body=@plan-summary.md >/dev/null
  else
    gh api -X POST "repos/$repo/issues/$pr_number/comments" -F body=@plan-summary.md >/dev/null
  fi
fi
