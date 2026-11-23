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

# Git Bash on Windows rewrites values starting with "/" into Windows paths for native programs, which
# would turn resource IDs such as /subscriptions/... into C:/Program Files/Git/subscriptions/....
export MSYS2_ENV_CONV_EXCL="${MSYS2_ENV_CONV_EXCL:+$MSYS2_ENV_CONV_EXCL;}TF_VAR_"

# Only the deployment pipeline changes environments, unless this one is deployed from a workstation.
# Checked after .env is read, because that's where local mode is set.
# shellcheck source=scripts/lib/deploy-guard.sh
source "$repo_root/scripts/lib/deploy-guard.sh"
may_change_environment "$deployment" "$@" || exit 1

missing=()
for name in ARM_SUBSCRIPTION_ID ARM_TENANT_ID TF_VAR_environment TF_STATE_RESOURCE_GROUP TF_STATE_STORAGE_ACCOUNT TF_STATE_CONTAINER; do
  [[ -n "${!name:-}" ]] || missing+=("$name")
done
if (( ${#missing[@]} )); then
  echo "Missing configuration: ${missing[*]}. See .env.example; the TF_STATE_* values come from scripts/bootstrap.sh." >&2
  exit 1
fi

# shellcheck source=scripts/lib/state-location.sh
source "$repo_root/scripts/lib/state-location.sh"
state_key="$(state_key "$deployment")"
state_container="$(state_container "$deployment")"

cd "$deployment_dir"
terraform init -input=false -reconfigure \
  -backend-config="resource_group_name=$TF_STATE_RESOURCE_GROUP" \
  -backend-config="storage_account_name=$TF_STATE_STORAGE_ACCOUNT" \
  -backend-config="container_name=$state_container" \
  -backend-config="key=$state_key" >/dev/null

terraform "$@"
