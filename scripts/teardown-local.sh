#!/usr/bin/env bash
# Removes what scripts/setup-local.sh created. By default the bootstrap (Terraform state storage,
# resource groups and custom roles) stays, so the environment can be recreated; --all removes it too.
# Local mode only: a pipeline environment is torn down by its own workflow (scripts/destroy.sh).
#
# Usage: scripts/teardown-local.sh [--all]
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_root/.env"
bootstrap_dir="$repo_root/infra/deployments/bootstrap"
# shellcheck source=scripts/lib/deploy-guard.sh
source "$repo_root/scripts/lib/deploy-guard.sh"
# shellcheck source=scripts/lib/env-file.sh
source "$repo_root/scripts/lib/env-file.sh"
# shellcheck source=scripts/lib/state-location.sh
source "$repo_root/scripts/lib/state-location.sh"

all=false
case "${1:-}" in
  "") ;;
  --all) all=true ;;
  *) echo "Usage: scripts/teardown-local.sh [--all]" >&2; exit 2 ;;
esac

[[ -f "$env_file" ]] || { echo "No .env; nothing to remove." >&2; exit 1; }
set -a
# shellcheck disable=SC1090
source "$env_file"
set +a
local_mode || { echo "This .env isn't a local-mode environment; use scripts/destroy.sh for a pipeline one." >&2; exit 1; }

tf() { "$repo_root/scripts/terraform.sh" "$@"; }
fail() { echo "$*" >&2; exit 1; }

what="platform and sign-in"
[[ "$all" == "true" ]] && what="platform, sign-in and bootstrap (everything)"
read -r -p "Remove the '${TF_VAR_environment:-dev}' environment's $what for '$TF_VAR_name_prefix'? Type the name prefix to confirm: " answer
[[ "$answer" == "$TF_VAR_name_prefix" ]] || { echo "Cancelled."; exit 1; }

# Lists a deployment's resources. A state file Azure confirms doesn't exist means the deployment was
# never created, so there's nothing to list. Failing to check, or to read a state file that exists,
# is an error: it's never taken to mean there's nothing there.
resources_of() {
  local deployment="$1" exists
  exists="$(az storage blob exists --auth-mode login --account-name "$TF_STATE_STORAGE_ACCOUNT" \
              --container-name "$(state_container "$deployment")" --name "$(state_key "$deployment")" \
              --query exists -o tsv)" || return 1
  case "${exists//[[:space:]]/}" in
    false) ;;
    true) tf "$deployment" state list ;;
    *) return 1 ;;
  esac
}

# Identity first: its configuration names the web app, which the platform's outputs still describe.
# Any failure stops the teardown with .env unchanged, so a rerun can finish.
if [[ -n "${TF_STATE_STORAGE_ACCOUNT:-}" ]]; then
  identity_state="$(resources_of identity)" ||
    fail "Couldn't read the sign-in (identity) state, so nothing was removed. Check access and rerun."
  platform_state="$(resources_of platform)" ||
    fail "Couldn't read the platform state, so nothing was removed. Check access and rerun."
  if [[ -z "$identity_state$platform_state" ]]; then
    echo "The platform and sign-in are already removed, or were never created."
  fi
  if [[ -n "$identity_state" ]]; then
    TF_VAR_web_app_url="$(tf platform output -raw web_app_url)" &&
      TF_VAR_app_identity_principal_id="$(tf platform output -raw app_identity_principal_id)" ||
      fail "The sign-in resources remain, but the platform outputs their configuration needs can't be read."
    export TF_VAR_web_app_url TF_VAR_app_identity_principal_id
    tf identity destroy -auto-approve
  fi
  if [[ -n "$platform_state" ]]; then
    tf platform destroy -auto-approve
  fi

  # Confirmed from the state, not assumed from the commands' exit codes.
  identity_state="$(resources_of identity)" && platform_state="$(resources_of platform)" ||
    fail "Couldn't confirm the platform and sign-in were removed. Rerun to check and finish."
  [[ -z "$identity_state$platform_state" ]] ||
    fail "Some resources remain after the destroy; rerun to finish: $(echo $identity_state $platform_state)"
elif [[ -s "$bootstrap_dir/terraform.tfstate" ]]; then
  echo "Only the bootstrap exists: an earlier setup stopped before moving its state into Azure."
else
  echo "No state storage in .env, so there's no platform or sign-in to remove."
fi

# These named what was just deleted; setup fills them in again.
for name in TF_VAR_eval_foundry_account_id TF_VAR_entra_client_id TF_VAR_restricted_group_id; do
  set_env_value "$env_file" "$name" ""
done

if [[ "$all" != "true" ]]; then
  echo
  echo "Removed. The bootstrap remains so you can run scripts/setup-local.sh again; scripts/teardown-local.sh --all removes it too."
  exit 0
fi

# The bootstrap's state lives in the storage account it manages, which is protected from deletion. So the
# state is moved to this machine, the storage is left out of Terraform's destroy, and its resource group
# is deleted afterwards, storage included. State already on this machine is the newest record: left by a
# removal that stopped part-way, or by a setup whose state never reached Azure. Either way it's used as is.
write_local_backend() {
  cat > backend_override.tf <<'EOF'
terraform {
  backend "local" {}
}
EOF
}

if [[ -n "${TF_STATE_STORAGE_ACCOUNT:-}" || -s "$bootstrap_dir/terraform.tfstate" ]]; then
  # Same handling as scripts/terraform.sh: empty values fall back to defaults, and Git Bash leaves ids alone.
  while IFS='=' read -r name _; do
    if [[ -z "${!name}" ]]; then unset "$name"; fi
  done < <(env | grep '^TF_VAR_' || true)
  export MSYS2_ENV_CONV_EXCL="${MSYS2_ENV_CONV_EXCL:+$MSYS2_ENV_CONV_EXCL;}TF_VAR_"

  cd "$bootstrap_dir"
  trap 'rm -f "$bootstrap_dir/backend_override.tf"' EXIT
  if [[ -s terraform.tfstate ]]; then
    echo "Removing the bootstrap using its state on this machine."
    write_local_backend
    terraform init -input=false -reconfigure >/dev/null
    # With no state storage in .env, the local state names it; it's checked like any other below.
    if [[ -z "${TF_STATE_STORAGE_ACCOUNT:-}" ]]; then
      TF_STATE_STORAGE_ACCOUNT="$(terraform output -raw state_storage_account_name)" &&
        TF_STATE_RESOURCE_GROUP="$(terraform output -raw state_resource_group_name)" ||
        fail "Couldn't read the state storage from $bootstrap_dir/terraform.tfstate. Nothing was removed."
    fi
    owns_state_locally || fail "The bootstrap state on this machine is kept. Nothing was removed."
  else
    owns_state_locally || exit 1
    terraform init -input=false -reconfigure \
      -backend-config="resource_group_name=$TF_STATE_RESOURCE_GROUP" \
      -backend-config="storage_account_name=$TF_STATE_STORAGE_ACCOUNT" \
      -backend-config="container_name=$TF_STATE_CONTAINER" \
      -backend-config="key=bootstrap.tfstate" >/dev/null
    write_local_backend
    terraform init -input=false -migrate-state -force-copy >/dev/null
  fi

  protected=(module.bootstrap.module.state.azurerm_storage_container.this
             module.bootstrap.module.state.azurerm_storage_container.operator
             module.bootstrap.module.state.azurerm_storage_account.this
             module.bootstrap.azurerm_resource_group.bootstrap)
  present="$(terraform state list)"
  for address in "${protected[@]}"; do
    if grep -qxF "$address" <<< "$present"; then
      terraform state rm "$address" >/dev/null
    fi
  done
  terraform destroy -auto-approve -input=false
  cd "$repo_root"
  removed_bootstrap=true
fi

bootstrap_group="rg-$TF_VAR_name_prefix-bootstrap"
exists="$(az group exists --name "$bootstrap_group")" ||
  fail "Couldn't check whether $bootstrap_group still exists. .env is unchanged; rerun to finish."
case "${exists//[[:space:]]/}" in
  true)
    # Deleted only as the last step of removing a bootstrap whose state this run read; a group no state
    # names may belong to something else.
    [[ "${removed_bootstrap:-}" == "true" ]] ||
      fail "$bootstrap_group exists, but neither .env nor this machine has state for it, so it was left alone."
    az group delete --name "$bootstrap_group" --yes ;;
  false) ;;
  *) fail "Unexpected answer checking $bootstrap_group: '$exists'. .env is unchanged; rerun to finish." ;;
esac
# Only now is the local copy of the bootstrap's state no longer needed.
rm -f "$bootstrap_dir/terraform.tfstate" "$bootstrap_dir/terraform.tfstate.backup"

# Nothing they name exists any more; a later setup starts from scratch.
for name in TF_STATE_RESOURCE_GROUP TF_STATE_STORAGE_ACCOUNT TF_STATE_CONTAINER TF_STATE_OPERATOR_CONTAINER; do
  set_env_value "$env_file" "$name" ""
done

echo
echo "Removed everything. Azure keeps the deleted Log Analytics workspace recoverable for 14 days and Entra"
echo "keeps the deleted app registration and group for 30, neither billed; both then go on their own."
