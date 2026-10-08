#!/usr/bin/env bash
# Keeps one open GitHub issue per recurring check: opens or updates it when the check finds a
# problem, and closes it when the problem is gone.
#
# Usage: scripts/report-issue.sh open  <label> <title> <body file>
#        scripts/report-issue.sh close <label>
set -euo pipefail

action="$1"
label="$2"

open_issue="$(gh issue list --label "$label" --state open --json number --jq '.[0].number // empty')"

case "$action" in
  open)
    title="$3"
    body_file="$4"
    gh label create "$label" --color D93F0B --force >/dev/null
    if [[ -n "$open_issue" ]]; then
      gh issue edit "$open_issue" --title "$title" --body-file "$body_file" >/dev/null
      echo "Updated issue #$open_issue"
    else
      gh issue create --label "$label" --title "$title" --body-file "$body_file"
    fi
    ;;
  close)
    if [[ -n "$open_issue" ]]; then
      gh issue close "$open_issue" --comment "Resolved: the latest scheduled check passed." >/dev/null
      echo "Closed issue #$open_issue"
    fi
    ;;
  *)
    echo "Unknown action '$action'. Use open or close." >&2
    exit 2
    ;;
esac
