#!/usr/bin/env bash
# Creates an environment from this machine and configures the app to run against it locally: no
# GitHub, no pipeline and no approval prompts. Safe to rerun; each step picks up where the last left off.
#
# Usage: cp .env.local.example .env   (fill in three values)
#        az login
#        scripts/setup-local.sh
# Requires: az, terraform, dotnet, jq and bash. See SETUP.md.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_root/.env"
web_project="$repo_root/src/PaymentOps.Web"
local_url="https://localhost:7207"
# shellcheck source=scripts/lib/env-file.sh
source "$repo_root/scripts/lib/env-file.sh"
# shellcheck source=scripts/lib/deploy-guard.sh
source "$repo_root/scripts/lib/deploy-guard.sh"

step() { printf '\n==> %s\n' "$*"; }
fail() { echo "$*" >&2; exit 1; }
tf() { "$repo_root/scripts/terraform.sh" "$@"; }

load_env() {
  set -a
  # shellcheck disable=SC1090
  source "$env_file"
  set +a
}

# Writes a default into .env only when the value is unset, so anything chosen there wins.
default() {
  local name="$1" value="$2"
  if [[ -z "${!name:-}" ]]; then
    set_env_value "$env_file" "$name" "$value"
  fi
}

# --- Checks: everything that can fail before anything is created ------------------------------

step "Checking tools and configuration"
for tool in az terraform dotnet jq; do
  command -v "$tool" >/dev/null 2>&1 || fail "$tool isn't installed or isn't on PATH. See SETUP.md."
done
[[ -f "$env_file" ]] || fail "No .env. Run: cp .env.local.example .env, then fill in its three values."
load_env
local_mode || fail "PAYMENTOPS_DEPLOY_FROM=local isn't set in .env. Start from .env.local.example."
missing=()
for name in TF_VAR_location TF_VAR_name_prefix TF_VAR_alert_email; do
  [[ -n "${!name:-}" ]] || missing+=("$name")
done
(( ${#missing[@]} == 0 )) || fail "Fill in ${missing[*]} in .env."
subscription="$(az account show --query id -o tsv 2>/dev/null)" || fail "The Azure CLI isn't signed in. Run: az login"

# Low-cost defaults; change any of them in .env before running.
default ARM_SUBSCRIPTION_ID "$subscription"
default ARM_TENANT_ID "$(az account show --query tenantId -o tsv)"
default TF_VAR_environment dev
default TF_VAR_app_service_sku F1
default TF_VAR_dotnet_version 10.0
default TF_VAR_storage_replication_type LRS
default TF_VAR_search_sku free
default TF_VAR_log_retention_days 30
default TF_VAR_log_daily_quota_gb 0.5
default TF_VAR_monthly_budget 10
default TF_VAR_model_retirement_buffer_days 90
default TF_VAR_local_app_urls "'[\"$local_url\"]'"
load_env

if [[ "$(az account show --query id -o tsv)" != "$ARM_SUBSCRIPTION_ID" ]]; then
  fail "The Azure CLI is on another subscription. Run: az account set --subscription $ARM_SUBSCRIPTION_ID"
fi

# A subscription may have only one free Search service. Check before creating anything, unless this
# environment already owns it from an earlier run.
if [[ "$TF_VAR_search_sku" == "free" && -n "${TF_STATE_STORAGE_ACCOUNT:-}" ]] &&
   tf platform output -raw search_service_id >/dev/null 2>&1; then
  : # This environment's own free service.
elif [[ "$TF_VAR_search_sku" == "free" ]]; then
  free="$(az rest --method get --query "value[?sku.name=='free'].name" -o tsv     --url "https://management.azure.com/subscriptions/$ARM_SUBSCRIPTION_ID/providers/Microsoft.Search/searchServices?api-version=2023-11-01")" ||
    fail "Couldn't list this subscription's Search services to check for a free one. Check your access and rerun."
  [[ -z "$free" ]] || fail "This subscription already has a free Search service ($free), and only one is allowed. \
Set TF_VAR_search_sku=basic in .env (billed hourly), or remove that service."
fi

# --- Infrastructure ---------------------------------------------------------------------------

plan_file="$repo_root/infra/deployments/platform/platform.tfplan"
# Saved plans hold sensitive values in plain text, so none outlives the run.
trap 'rm -f "$plan_file"' EXIT

retry_seconds="${SETUP_RETRY_SECONDS:-60}"

# Plans, checks model quota and regional capacity against that plan, then applies exactly that plan.
# Custom roles and role assignments made moments earlier can take minutes to become visible, so a
# failed apply is planned and applied again; what already exists is kept.
apply_platform() {
  local attempt
  for attempt in 1 2 3; do
    tf platform plan -input=false -out=platform.tfplan
    "$repo_root/scripts/check-model-capacity.sh" platform platform.tfplan
    if tf platform apply -input=false platform.tfplan; then
      rm -f "$plan_file"
      return 0
    fi
    rm -f "$plan_file"
    (( attempt < 3 )) || fail "The platform apply failed three times. Rerun scripts/setup-local.sh; it carries on from here."
    echo "Apply failed; new roles can take a few minutes to appear. Retrying in $retry_seconds seconds (attempt $attempt of 3)."
    sleep "$retry_seconds"
  done
}

step "1/7 Bootstrap: Terraform state, the resource group and your developer roles"
"$repo_root/scripts/bootstrap.sh"
load_env

step "2/7 Platform: Foundry with its models, AI Search, storage, monitoring and the web app"
apply_platform

step "3/7 Bootstrap again: screening rights on the new Foundry account"
TF_VAR_eval_foundry_account_id="$(tf platform output -raw foundry_account_id)"
export TF_VAR_eval_foundry_account_id
set_env_value "$env_file" TF_VAR_eval_foundry_account_id "$TF_VAR_eval_foundry_account_id"
"$repo_root/scripts/bootstrap.sh"
load_env

step "4/7 Knowledge: building the corpus and publishing it to AI Search"
corpus="$(mktemp -d)"
dotnet run --project "$repo_root/tools/PaymentOps.CorpusBuild" --configuration Release -- \
  "$repo_root/data/knowledge/src" --out "$corpus"
search_endpoint="$(tf platform output -raw search_endpoint)"
# Search roles granted moments ago can take several minutes to take effect.
for attempt in {1..10}; do
  if dotnet run --project "$repo_root/tools/PaymentOps.KnowledgePublish" --configuration Release -- \
       "$corpus" --endpoint "$search_endpoint" --ai "$repo_root/ai" --ensure-evaluation-index; then
    break
  fi
  (( attempt < 10 )) || fail "Publishing the knowledge failed. Rerun scripts/setup-local.sh; it resumes from here."
  echo "Search access may not be active yet; retrying in 30 seconds (attempt $attempt of 10)."
  sleep 30
done
rm -rf "$corpus"

step "5/7 Sign-in: the Entra app registration, its roles and a local sign-in secret"
"$repo_root/scripts/identity.sh" -auto-approve
load_env

step "6/7 Platform again: the web app's sign-in settings"
apply_platform

step "7/7 App settings: writing user-secrets for src/PaymentOps.Web"
# Read first, so a failed read stops the script instead of writing an empty setting.
client_id="$(tf identity output -raw client_id)"
restricted_group_id="$(tf identity output -raw restricted_group_id)"
foundry_endpoint="$(tf platform output -raw foundry_endpoint)"
content_safety_endpoint="$(tf platform output -raw content_safety_endpoint)"
secrets() { dotnet user-secrets --project "$web_project" "$@" >/dev/null; }
secrets set AzureAd:TenantId "$ARM_TENANT_ID"
secrets set AzureAd:ClientId "$client_id"
secrets set AzureAd:ClientCredentials:0:SourceType ClientSecret
# The secret goes from Terraform's output straight into user-secrets: never an argument, variable or log line.
tf identity output -json local_client_secret |
  jq '{"AzureAd:ClientCredentials:0:ClientSecret": .}' |
  dotnet user-secrets --project "$web_project" set >/dev/null
secrets set PaymentOps:Foundry:Endpoint "$foundry_endpoint"
secrets set PaymentOps:ContentSafety:Endpoint "$content_safety_endpoint"
secrets set PaymentOps:Search:Endpoint "$search_endpoint"
secrets set PaymentOps:Authorization:RestrictedGroupId "$restricted_group_id"

cat <<EOF

Done. Start the app:

  dotnet run --project src/PaymentOps.Web --launch-profile https

then open $local_url and sign in with your own account (you have the Admin role).
EOF
if [[ "${TF_VAR_create_demo_users:-}" == "true" ]]; then
  echo "Demo users' sign-ins: scripts/terraform.sh identity output -json demo_user_sign_ins"
fi
