#!/usr/bin/env bash
# Runs Terraform for one deployment with configuration from .env and the remote backend.
#
# Usage: scripts/terraform.sh <deployment> <terraform command> [arguments]
#   scripts/terraform.sh platform plan
#   scripts/terraform.sh platform apply
#
# The bootstrap has its own first-run logic; use scripts/bootstrap.sh for it.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_root/.env"

deployment="${1:-}"
shift || true
if [[ -z "$deployment" || $# -eq 0 ]]; then
  echo "Usage: scripts/terraform.sh <deployment> <terraform command> [arguments]" >&2
  exit 2
fi

deployment_dir="$repo_root/infra/deployments/$deployment"
if [[ ! -d "$deployment_dir" ]]; then
  echo "Unknown deployment '$deployment'. Available: $(ls "$repo_root/infra/deployments" | tr '\n' ' ')" >&2
  exit 2
fi

# Only the deployment pipeline changes environments. Workstations can plan and inspect; the
# bootstrap is the one exception because it creates the pipeline's own identities and state.
changes_environment=false
case "$1" in
  apply | destroy | import | taint | untaint | force-unlock) changes_environment=true ;;
  state) [[ "${2:-}" =~ ^(rm|mv|push|replace-provider)$ ]] && changes_environment=true ;;
esac
if [[ "$changes_environment" == "true" && "$deployment" != "bootstrap" && "${GITHUB_ACTIONS:-}" != "true" ]]; then
  echo "'terraform $*' for '$deployment' runs only in the deployment pipeline. Use 'plan' locally." >&2
  exit 1
fi

# .env fills in configuration; anything already set in the environment (such as CI variables) wins.
if [[ -f "$env_file" ]]; then
  preset_env="$(export -p | grep -v '^declare -[a-z]*r')"
  set -a
  # shellcheck disable=SC1090
  source "$env_file"
  set +a
  eval "$preset_env"
fi

# Empty TF_VAR_* lines would reach Terraform as empty strings; unset them so defaults apply.
while IFS='=' read -r name _; do
  if [[ -z "${!name}" ]]; then unset "$name"; fi
done < <(env | grep '^TF_VAR_' || true)

missing=()
for name in ARM_SUBSCRIPTION_ID ARM_TENANT_ID TF_VAR_environment TF_STATE_RESOURCE_GROUP TF_STATE_STORAGE_ACCOUNT TF_STATE_CONTAINER; do
  [[ -n "${!name:-}" ]] || missing+=("$name")
done
if (( ${#missing[@]} )); then
  echo "Missing configuration: ${missing[*]}. See .env.example; the TF_STATE_* values come from scripts/bootstrap.sh." >&2
  exit 1
fi

# One state file per deployment and environment.
state_key="$TF_VAR_environment/$deployment.tfstate"
[[ "$deployment" == "bootstrap" ]] && state_key="bootstrap.tfstate"

cd "$deployment_dir"
terraform init -input=false -reconfigure \
  -backend-config="resource_group_name=$TF_STATE_RESOURCE_GROUP" \
  -backend-config="storage_account_name=$TF_STATE_STORAGE_ACCOUNT" \
  -backend-config="container_name=$TF_STATE_CONTAINER" \
  -backend-config="key=$state_key" >/dev/null

terraform "$@"
