#!/usr/bin/env bash
# Tests scripts/lib/deploy-guard.sh: which Terraform commands may change an environment from where.
# Usage: bash scripts/tests/deploy-guard-test.sh
set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
failures=0

# Each case runs in a clean subshell so one case's variables can't leak into the next.
expect() {
  local want="$1" description="$2"
  shift 2
  local got
  ( unset GITHUB_ACTIONS PAYMENTOPS_DEPLOY_FROM TF_VAR_github_repository
    # shellcheck source=scripts/lib/deploy-guard.sh
    source "$repo_root/scripts/lib/deploy-guard.sh"
    eval "$*" ) >/dev/null 2>&1
  got=$?
  if [[ "$got" == "$want" ]]; then
    echo "ok   $description"
  else
    echo "FAIL $description: exit $got, expected $want"
    failures=$((failures + 1))
  fi
}

# Pipeline mode (no switch), on a workstation.
expect 1 "pipeline mode refuses platform apply"        may_change_environment platform apply
expect 0 "pipeline mode allows platform plan"          may_change_environment platform plan
expect 0 "pipeline mode allows bootstrap apply"        may_change_environment bootstrap apply
expect 0 "pipeline mode allows identity apply"         may_change_environment identity apply
expect 1 "pipeline mode refuses platform state mv"     may_change_environment platform state mv a b
expect 0 "pipeline mode allows platform state list"    may_change_environment platform state list
expect 1 "pipeline mode refuses platform destroy"      may_change_environment platform destroy
expect 1 "local_mode is off without the switch"        local_mode

# In GitHub Actions.
expect 0 "Actions may apply the platform"              GITHUB_ACTIONS=true may_change_environment platform apply

# Local mode.
expect 0 "local mode allows platform apply"            PAYMENTOPS_DEPLOY_FROM=local may_change_environment platform apply
expect 0 "local mode allows platform destroy"          PAYMENTOPS_DEPLOY_FROM=local may_change_environment platform destroy
expect 0 "local_mode is on with the switch"            PAYMENTOPS_DEPLOY_FROM=local local_mode

# Local mode against a pipeline environment is refused, naming the variable.
expect 1 "local mode refused with a repository"        PAYMENTOPS_DEPLOY_FROM=local TF_VAR_github_repository=owner/name local_mode
expect 1 "apply refused with switch and repository"    PAYMENTOPS_DEPLOY_FROM=local TF_VAR_github_repository=owner/name may_change_environment platform apply
expect 0 "refusal names TF_VAR_github_repository" \
  'message=$( (PAYMENTOPS_DEPLOY_FROM=local TF_VAR_github_repository=owner/name local_mode) 2>&1 ); grep -q TF_VAR_github_repository <<< "$message"'

if (( failures )); then
  echo "$failures failed"
  exit 1
fi
echo "all passed"
