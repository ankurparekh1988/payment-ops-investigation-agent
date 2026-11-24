#!/usr/bin/env bash
# Tests scripts/lib/deploy-guard.sh: which Terraform commands may change an environment from where.
# Azure is replaced by a stub: AZ_TAG is the state storage's deploy-from mark, AZ_FAIL makes the read fail.
# Usage: bash scripts/tests/deploy-guard-test.sh
set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
failures=0

stubs="$(mktemp -d)"
trap 'rm -rf "$stubs"' EXIT
cat > "$stubs/az" <<'STUB'
#!/usr/bin/env bash
[[ -n "${AZ_FAIL:-}" ]] && exit 1
[[ "$*" == "storage account show "* ]] && printf '%s\r\n' "${AZ_TAG:-}"
exit 0
STUB
chmod +x "$stubs/az"

# The variables a GitHub Actions job with OIDC sign-in has.
actions='GITHUB_ACTIONS=true ARM_USE_OIDC=true ARM_CLIENT_ID=deploy ACTIONS_ID_TOKEN_REQUEST_URL=https://token ACTIONS_ID_TOKEN_REQUEST_TOKEN=t'
# A local-mode environment with state storage recorded in .env.
local_env='PAYMENTOPS_DEPLOY_FROM=local TF_STATE_STORAGE_ACCOUNT=sttf TF_STATE_RESOURCE_GROUP=rg-state'

# Each case runs in a clean subshell so one case's variables can't leak into the next.
expect() {
  local want="$1" description="$2"
  shift 2
  local got
  ( unset GITHUB_ACTIONS PAYMENTOPS_DEPLOY_FROM TF_VAR_github_repository ARM_USE_OIDC ARM_CLIENT_ID ARM_USE_CLI \
          ACTIONS_ID_TOKEN_REQUEST_URL ACTIONS_ID_TOKEN_REQUEST_TOKEN TF_STATE_STORAGE_ACCOUNT TF_STATE_RESOURCE_GROUP
    PATH="$stubs:$PATH"
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
expect 0 "pipeline mode allows a destroy plan"         may_change_environment platform plan -destroy -out=destroy.tfplan
expect 0 "pipeline mode allows platform output"        may_change_environment platform output -raw web_app_url
expect 0 "pipeline mode allows platform show"          may_change_environment platform show -json platform.tfplan
expect 0 "pipeline mode allows platform state list"    may_change_environment platform state list
expect 1 "pipeline mode refuses platform state mv"     may_change_environment platform state mv a b
expect 1 "pipeline mode refuses platform destroy"      may_change_environment platform destroy
expect 0 "pipeline mode allows bootstrap apply"        TF_VAR_github_repository=owner/name may_change_environment bootstrap apply
expect 0 "pipeline mode allows identity apply"         may_change_environment identity apply
expect 1 "local_mode is off without the switch"        local_mode

# Bypasses: anything not known to only read counts as a change.
expect 1 "-chdir before apply is refused"              may_change_environment platform -chdir=. apply
expect 1 "refresh is refused"                          may_change_environment platform refresh
expect 1 "apply -refresh-only is refused"              may_change_environment platform apply -refresh-only
expect 1 "workspace new is refused"                    may_change_environment platform workspace new other
expect 1 "init -migrate-state is refused"              may_change_environment platform init -migrate-state
expect 1 "terraform test is refused"                   may_change_environment platform test
expect 1 "state push is refused"                       may_change_environment platform state push old.tfstate
expect 1 "bootstrap apply without the repository is refused, as it would remove the pipeline identities" \
                                                       may_change_environment bootstrap apply

# In GitHub Actions: the job's OIDC sign-in, not GITHUB_ACTIONS alone.
expect 0 "Actions with OIDC may apply the platform"    "$actions may_change_environment platform apply -input=false platform.tfplan"
expect 0 "Actions sign-in turns the Azure CLI fallback off" \
  "$actions; export ARM_USE_CLI=true; may_change_environment platform apply && [[ \$ARM_USE_CLI == false ]]"
expect 1 "GITHUB_ACTIONS=true on a workstation is refused" GITHUB_ACTIONS=true may_change_environment platform apply
expect 1 "Actions without an OIDC token is refused"    "GITHUB_ACTIONS=true ARM_USE_OIDC=true ARM_CLIENT_ID=deploy may_change_environment platform apply"

# Local mode: only on state storage Azure confirms is marked deploy-from=local.
expect 0 "local mode applies its own environment"      "$local_env AZ_TAG=local may_change_environment platform apply"
expect 0 "local mode destroys its own environment"     "$local_env AZ_TAG=local may_change_environment platform destroy"
expect 0 "local mode reads without asking Azure"       "$local_env AZ_FAIL=1 may_change_environment platform state list"
expect 1 "local mode refused on unmarked (pipeline) state" "$local_env AZ_TAG= may_change_environment platform apply"
expect 1 "local mode refused when the mark can't be read" "$local_env AZ_FAIL=1 may_change_environment platform apply"
expect 1 "local mode refused with no state storage in .env" "PAYMENTOPS_DEPLOY_FROM=local AZ_TAG=local may_change_environment platform apply"
expect 1 "local mode refused for a bootstrap on pipeline state" "$local_env AZ_TAG= may_change_environment bootstrap apply"
expect 0 "local_mode is on with the switch"            PAYMENTOPS_DEPLOY_FROM=local local_mode

# Local mode against a pipeline environment is refused, naming the variable.
expect 1 "local mode refused with a repository"        PAYMENTOPS_DEPLOY_FROM=local TF_VAR_github_repository=owner/name local_mode
expect 1 "apply refused with switch and repository"    "$local_env AZ_TAG=local TF_VAR_github_repository=owner/name may_change_environment platform apply"
expect 0 "refusal names TF_VAR_github_repository" \
  'message=$( (PAYMENTOPS_DEPLOY_FROM=local TF_VAR_github_repository=owner/name local_mode) 2>&1 ); grep -q TF_VAR_github_repository <<< "$message"'

if (( failures )); then
  echo "$failures failed"
  exit 1
fi
echo "all passed"
