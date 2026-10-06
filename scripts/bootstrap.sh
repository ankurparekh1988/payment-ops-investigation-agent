#!/usr/bin/env bash
# One-time setup: creates Terraform state storage, the workload resource group and the GitHub
# pipeline identities. The first run starts with local state, then moves it into the storage
# account it has just created. Later runs use that remote state directly.
#
# Usage: scripts/bootstrap.sh          (reads configuration from .env in the repository root)
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
stack_dir="$repo_root/infra/deployments/bootstrap"
env_file="$repo_root/.env"
state_key="bootstrap.tfstate"

if [[ -f "$env_file" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$env_file"
  set +a
fi

missing=()
for name in ARM_SUBSCRIPTION_ID ARM_TENANT_ID TF_VAR_location TF_VAR_name_prefix TF_VAR_environment \
            TF_VAR_github_repository TF_VAR_github_owner_id TF_VAR_github_repository_id; do
  [[ -n "${!name:-}" ]] || missing+=("$name")
done
if (( ${#missing[@]} )); then
  echo "Missing configuration: ${missing[*]}" >&2
  echo "Copy .env.example to .env and fill it in." >&2
  exit 1
fi

# Makes sure the Azure CLI session matches the configured subscription before changing anything.
current_subscription="$(az account show --query id -o tsv)"
if [[ "$current_subscription" != "$ARM_SUBSCRIPTION_ID" ]]; then
  echo "Azure CLI is signed in to $current_subscription, but ARM_SUBSCRIPTION_ID is $ARM_SUBSCRIPTION_ID." >&2
  echo "Run: az account set --subscription $ARM_SUBSCRIPTION_ID" >&2
  exit 1
fi

# A federated credential with the wrong subject is created without error and only fails when a
# workflow tries to sign in, so compare against the prefix GitHub actually issues when possible.
expected_prefix="repo:${TF_VAR_github_repository%%/*}@${TF_VAR_github_owner_id}/${TF_VAR_github_repository#*/}@${TF_VAR_github_repository_id}"
if command -v gh >/dev/null 2>&1; then
  actual_prefix="$(gh api "repos/$TF_VAR_github_repository/actions/oidc/customization/sub" --jq '.sub_claim_prefix // empty' 2>/dev/null || true)"
  if [[ -z "$actual_prefix" ]]; then
    echo "Couldn't read the OIDC subject prefix from GitHub (is gh signed in?); skipping the check."
    echo "Expected prefix: $expected_prefix"
  elif [[ "$actual_prefix" != "$expected_prefix" ]]; then
    echo "GitHub issues OIDC subjects starting with '$actual_prefix'," >&2
    echo "but the configuration would trust '$expected_prefix'. Check TF_VAR_github_* in .env." >&2
    exit 1
  else
    echo "OIDC subject prefix matches GitHub: $actual_prefix"
  fi
else
  echo "GitHub CLI not found; skipping the OIDC subject check. Expected prefix: $expected_prefix"
fi

# Writes or replaces KEY=value in .env.
set_env_value() {
  local key="$1" value="$2"
  touch "$env_file"
  if grep -q "^${key}=" "$env_file"; then
    sed -i.bak "s|^${key}=.*|${key}=${value}|" "$env_file" && rm -f "$env_file.bak"
  else
    printf '%s=%s\n' "$key" "$value" >> "$env_file"
  fi
}

backend_args() {
  echo "-backend-config=resource_group_name=$TF_STATE_RESOURCE_GROUP" \
       "-backend-config=storage_account_name=$TF_STATE_STORAGE_ACCOUNT" \
       "-backend-config=container_name=$TF_STATE_CONTAINER" \
       "-backend-config=key=$state_key"
}

cd "$stack_dir"

if [[ -n "${TF_STATE_STORAGE_ACCOUNT:-}" ]]; then
  echo "State storage already exists ($TF_STATE_STORAGE_ACCOUNT); applying against remote state."
  # shellcheck disable=SC2046
  terraform init -input=false -reconfigure $(backend_args)
  terraform apply
  exit 0
fi

echo "First run: creating state storage with local state."
cat > backend_override.tf <<'EOF'
# Temporary, written by scripts/bootstrap.sh for the first run only.
terraform {
  backend "local" {}
}
EOF
trap 'rm -f "$stack_dir/backend_override.tf"' EXIT

terraform init -input=false
terraform apply

TF_STATE_RESOURCE_GROUP="$(terraform output -raw state_resource_group_name)"
TF_STATE_STORAGE_ACCOUNT="$(terraform output -raw state_storage_account_name)"
TF_STATE_CONTAINER="$(terraform output -raw state_container_name)"

set_env_value TF_STATE_RESOURCE_GROUP "$TF_STATE_RESOURCE_GROUP"
set_env_value TF_STATE_STORAGE_ACCOUNT "$TF_STATE_STORAGE_ACCOUNT"
set_env_value TF_STATE_CONTAINER "$TF_STATE_CONTAINER"

rm -f backend_override.tf
echo "Moving bootstrap state into $TF_STATE_STORAGE_ACCOUNT/$TF_STATE_CONTAINER/$state_key."

# A new role assignment can take a few minutes to take effect, so retry the migration.
for attempt in {1..10}; do
  # shellcheck disable=SC2046
  if terraform init -input=false -migrate-state -force-copy $(backend_args); then
    break
  fi
  if (( attempt == 10 )); then
    echo "State migration failed. Local state is kept in $stack_dir/terraform.tfstate; re-run this script." >&2
    exit 1
  fi
  echo "Storage access not active yet; retrying in 30 seconds (attempt $attempt of 10)."
  sleep 30
done

rm -f terraform.tfstate terraform.tfstate.backup
echo
echo "Bootstrap complete. Values for GitHub repository variables:"
terraform output
