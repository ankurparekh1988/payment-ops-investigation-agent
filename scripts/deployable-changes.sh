#!/usr/bin/env bash
# Decides whether a push changed anything CD deploys: the app, the infrastructure or the knowledge index.
# Only paths known to reach none of them are skipped, so a path nobody listed is deployed, and so is a
# push whose previous commit can't be read.
#
# Usage: scripts/deployable-changes.sh <previous commit> <pushed commit>
#   Prints deploy=true|false and a reason, and appends them to GITHUB_OUTPUT when it's set.
set -euo pipefail

before="$1"
after="$2"

emit() {
  printf 'deploy=%s\nreason=%s\n' "$1" "$2" | tee -a "${GITHUB_OUTPUT:-/dev/null}"
}

# Documentation, tests, the evaluation suite and the other workflows never reach the deployed environment.
# Markdown elsewhere is content: prompts under ai/ and runbooks under data/knowledge/.
not_deployed() {
  case "$1" in
    .github/workflows/cd.yml) return 1 ;;
    docs/* | tests/* | eval/* | .github/workflows/* | .github/pull_request_template.md) return 0 ;;
    README.md | LICENSE | .checkov.yaml | .env.example) return 0 ;;
    *) return 1 ;;
  esac
}

# A new branch reports an all-zero previous commit.
if [[ ! "$before" =~ ^[0-9a-f]{40}$ || "$before" =~ ^0+$ ]] || ! git cat-file -e "$before^{commit}" 2>/dev/null; then
  emit true "the previous commit isn't available, so everything is deployed"
  exit 0
fi

while IFS= read -r path; do
  if [[ -n "$path" ]] && ! not_deployed "$path"; then
    emit true "$path changed"
    exit 0
  fi
done < <(git -c core.quotePath=false diff --no-renames --name-only "$before" "$after")

emit false "only documentation, tests, the evaluation suite or other workflows changed"
