#!/usr/bin/env bash
# Configures the GitHub repository for the pipelines, from .env and the bootstrap outputs:
#   - secrets: identifiers and personal values, so workflow logs mask them
#   - TERRAFORM_SETTINGS: every other setting, as one JSON variable
#   - environments: <env>-infra (requires approval) and <env>, deployable from main only
#   - evaluation environments: <env>-eval and <env>-eval-publish (require approval, any branch, so pull
#     requests can be evaluated) and their -scheduled pair (no approval, protected branches only), with
#     the platform endpoints they need as environment variables
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
platform_output() { "$repo_root/scripts/terraform.sh" platform output -raw "$1"; }

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
# Empty until the bootstrap has been re-run with the evaluation identities.
set_secret AZURE_EVAL_CLIENT_ID "$(bootstrap_output eval_identity_client_id 2>/dev/null || true)"
set_secret AZURE_EVAL_PUBLISH_CLIENT_ID "$(bootstrap_output eval_publish_identity_client_id 2>/dev/null || true)"
set_secret ALERT_EMAIL "${TF_VAR_alert_email:-}"
set_secret DEVELOPER_OBJECT_IDS "${TF_VAR_developer_object_ids:-}"
set_secret BOOTSTRAP_OPERATOR_OBJECT_ID "${TF_VAR_bootstrap_operator_object_id:-}"

# Created once and never rotated by this script, so a plan encrypted before a rerun can still be
# applied. Delete the secret to rotate it.
if ! gh secret list --repo "$repo" --json name --jq '.[].name' | grep -qx PLAN_ENCRYPTION_KEY; then
  set_secret PLAN_ENCRYPTION_KEY "$(openssl rand -base64 48)"
fi

# --- Settings ----------------------------------------------------------------------------------

required_settings=(
  TF_STATE_RESOURCE_GROUP TF_STATE_STORAGE_ACCOUNT TF_STATE_CONTAINER
  TF_VAR_location TF_VAR_name_prefix TF_VAR_environment TF_VAR_app_service_sku TF_VAR_dotnet_version
  TF_VAR_storage_replication_type TF_VAR_search_sku TF_VAR_log_retention_days TF_VAR_log_daily_quota_gb
  TF_VAR_monthly_budget TF_VAR_model_retirement_buffer_days
)
# Written by scripts/identity.sh, which needs a deployed platform, so empty on a first run.
identity_settings=(TF_VAR_entra_client_id TF_VAR_restricted_group_id)

settings="{"
add_setting() { settings+="\"$1\":\"${2//\"/\\\"}\","; }
for key in "${required_settings[@]}"; do add_setting "$key" "${!key:?Set $key in .env}"; done
for key in "${identity_settings[@]}"; do [[ -z "${!key:-}" ]] || add_setting "$key" "${!key}"; done
settings="${settings%,}}"
gh variable set TERRAFORM_SETTINGS --repo "$repo" --body "$settings"

# Individual variables from earlier versions of this script are no longer read.
for name in $(gh variable list --repo "$repo" --json name --jq '.[].name' | grep -E '^(TF_VAR_|TF_STATE_)' || true); do
  gh variable delete "$name" --repo "$repo"
done

# --- Environments ------------------------------------------------------------------------------

reviewer_id="$(gh api user --jq .id)"
reviewed="[{\"type\":\"User\",\"id\":$reviewer_id}]"
protected='{ "protected_branches": true, "custom_branch_policies": false }'

configure_environment() {
  local name="$1" reviewers="$2" branch_policy="$3"
  gh api -X PUT "repos/$repo/environments/$name" --input - >/dev/null <<EOF
{
  "reviewers": $reviewers,
  "prevent_self_review": false,
  "deployment_branch_policy": $branch_policy
}
EOF
  echo "Environment $name configured"
}

configure_environment "$environment-infra" "$reviewed" "$protected"
configure_environment "$environment" '[]' "$protected"

# --- Evaluation environments -------------------------------------------------------------------

# The reviewed pair allows any branch, because pull request refs must be able to deploy to it; the
# reviewer's approval is the control. The scheduled pair needs no approval, so it's limited to protected branches.
configure_environment "$environment-eval" "$reviewed" null
configure_environment "$environment-eval-publish" "$reviewed" null
configure_environment "$environment-eval-scheduled" '[]' "$protected"
configure_environment "$environment-eval-publish-scheduled" '[]' "$protected"

# The endpoints exist once the platform is deployed; rerun this script after that, and after a rebuild.
if search_endpoint="$(platform_output search_endpoint 2>/dev/null)"; then
  foundry_endpoint="$(platform_output foundry_endpoint)"
  # Missing until the platform has been applied with it; the other endpoints are still refreshed.
  content_safety_endpoint="$(platform_output content_safety_endpoint 2>/dev/null || true)"
  ingestion_endpoint="$(platform_output evaluation_ingestion_endpoint)"
  rule_immutable_id="$(platform_output evaluation_dcr_immutable_id)"
  for name in "$environment-eval" "$environment-eval-scheduled"; do
    gh variable set EVAL_FOUNDRY_ENDPOINT --env "$name" --repo "$repo" --body "$foundry_endpoint"
    if [[ -n "$content_safety_endpoint" ]]; then
      gh variable set EVAL_CONTENT_SAFETY_ENDPOINT --env "$name" --repo "$repo" --body "$content_safety_endpoint"
    fi
    gh variable set EVAL_SEARCH_ENDPOINT --env "$name" --repo "$repo" --body "$search_endpoint"
  done
  for name in "$environment-eval-publish" "$environment-eval-publish-scheduled"; do
    gh variable set EVAL_SEARCH_ENDPOINT --env "$name" --repo "$repo" --body "$search_endpoint"
    gh variable set EVAL_INGESTION_ENDPOINT --env "$name" --repo "$repo" --body "$ingestion_endpoint"
    gh variable set EVAL_DCR_IMMUTABLE_ID --env "$name" --repo "$repo" --body "$rule_immutable_id"
  done
else
  echo "Skipping evaluation environment variables: the platform isn't deployed yet"
fi
