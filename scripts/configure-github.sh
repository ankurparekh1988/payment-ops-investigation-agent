#!/usr/bin/env bash
# Copies configuration into the GitHub repository's Actions secrets and variables, from .env and
# the bootstrap outputs. Identifiers and personal values become secrets so workflow logs mask
# them; everything else becomes a variable.
#
# Usage: scripts/configure-github.sh   (requires the GitHub CLI, signed in, and a completed bootstrap)
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_root/.env"

if [[ -f "$env_file" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$env_file"
  set +a
fi

repo="${TF_VAR_github_repository:?Set TF_VAR_github_repository in .env}"
bootstrap_output() { "$repo_root/scripts/terraform.sh" bootstrap output -raw "$1"; }

set_secret() {
  if [[ -n "$2" ]]; then
    gh secret set "$1" --repo "$repo" --body "$2"
  else
    echo "Skipping secret $1: no value"
  fi
}

set_variable() {
  if [[ -n "$2" ]]; then
    gh variable set "$1" --repo "$repo" --body "$2"
  else
    echo "Skipping variable $1: no value"
  fi
}

set_secret AZURE_TENANT_ID "$ARM_TENANT_ID"
set_secret AZURE_SUBSCRIPTION_ID "$ARM_SUBSCRIPTION_ID"
set_secret AZURE_PLAN_CLIENT_ID "$(bootstrap_output plan_identity_client_id)"
set_secret AZURE_DEPLOY_CLIENT_ID "$(bootstrap_output deploy_identity_client_id)"
set_secret ALERT_EMAIL "${TF_VAR_alert_email:-}"
set_secret DEVELOPER_OBJECT_IDS "${TF_VAR_developer_object_ids:-}"
set_secret BOOTSTRAP_OPERATOR_OBJECT_ID "${TF_VAR_bootstrap_operator_object_id:-}"

set_variable TF_STATE_RESOURCE_GROUP "$TF_STATE_RESOURCE_GROUP"
set_variable TF_STATE_STORAGE_ACCOUNT "$TF_STATE_STORAGE_ACCOUNT"
set_variable TF_STATE_CONTAINER "$TF_STATE_CONTAINER"

for name in location name_prefix environment app_service_sku dotnet_version storage_replication_type \
            log_retention_days log_daily_quota_gb monthly_budget model_retirement_buffer_days; do
  env_name="TF_VAR_$name"
  set_variable "TF_VAR_${name^^}" "${!env_name:-}"
done
