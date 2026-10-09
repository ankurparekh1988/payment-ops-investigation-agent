#!/usr/bin/env bash
# Configures the GitHub repository for the pipelines, from .env and the bootstrap outputs:
#   - secrets: identifiers and personal values, so workflow logs mask them
#   - TERRAFORM_SETTINGS: every other setting, as one JSON variable
#   - environments: <env>-infra (requires approval) and <env>, deployable from main only
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
environment="${TF_VAR_environment:?Set TF_VAR_environment in .env}"
bootstrap_output() { "$repo_root/scripts/terraform.sh" bootstrap output -raw "$1"; }

set_secret() {
  if [[ -n "$2" ]]; then
    gh secret set "$1" --repo "$repo" --body "$2"
  else
    echo "Skipping secret $1: no value"
  fi
}

# --- Secrets -----------------------------------------------------------------------------------

set_secret AZURE_TENANT_ID "$ARM_TENANT_ID"
set_secret AZURE_SUBSCRIPTION_ID "$ARM_SUBSCRIPTION_ID"
set_secret AZURE_PLAN_CLIENT_ID "$(bootstrap_output plan_identity_client_id)"
set_secret AZURE_DEPLOY_CLIENT_ID "$(bootstrap_output deploy_identity_client_id)"
set_secret ALERT_EMAIL "${TF_VAR_alert_email:-}"
set_secret DEVELOPER_OBJECT_IDS "${TF_VAR_developer_object_ids:-}"
set_secret BOOTSTRAP_OPERATOR_OBJECT_ID "${TF_VAR_bootstrap_operator_object_id:-}"

# Created once and never rotated by this script, so a plan encrypted before a rerun can still be
# applied. Delete the secret to rotate it.
if ! gh secret list --repo "$repo" --json name --jq '.[].name' | grep -qx PLAN_ENCRYPTION_KEY; then
  set_secret PLAN_ENCRYPTION_KEY "$(openssl rand -base64 48)"
fi

# --- Settings ----------------------------------------------------------------------------------

settings_keys=(
  TF_STATE_RESOURCE_GROUP TF_STATE_STORAGE_ACCOUNT TF_STATE_CONTAINER
  TF_VAR_location TF_VAR_name_prefix TF_VAR_environment TF_VAR_app_service_sku TF_VAR_dotnet_version
  TF_VAR_storage_replication_type TF_VAR_log_retention_days TF_VAR_log_daily_quota_gb
  TF_VAR_monthly_budget TF_VAR_model_retirement_buffer_days TF_VAR_entra_client_id TF_VAR_restricted_group_id
)
settings="{"
for key in "${settings_keys[@]}"; do
  value="${!key:?Set $key in .env}"
  settings+="\"$key\":\"${value//\"/\\\"}\","
done
settings="${settings%,}}"
gh variable set TERRAFORM_SETTINGS --repo "$repo" --body "$settings"

# Individual variables from earlier versions of this script are no longer read.
for name in $(gh variable list --repo "$repo" --json name --jq '.[].name' | grep -E '^(TF_VAR_|TF_STATE_)' || true); do
  gh variable delete "$name" --repo "$repo"
done

# --- Environments ------------------------------------------------------------------------------

reviewer_id="$(gh api user --jq .id)"
for name in "$environment-infra" "$environment"; do
  reviewers='[]'
  [[ "$name" == "$environment-infra" ]] && reviewers="[{\"type\":\"User\",\"id\":$reviewer_id}]"
  gh api -X PUT "repos/$repo/environments/$name" --input - >/dev/null <<EOF
{
  "reviewers": $reviewers,
  "prevent_self_review": false,
  "deployment_branch_policy": { "protected_branches": true, "custom_branch_policies": false }
}
EOF
  echo "Environment $name configured"
done
