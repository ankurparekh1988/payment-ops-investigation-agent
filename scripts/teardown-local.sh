#!/usr/bin/env bash
# Removes what scripts/setup-local.sh created. By default the bootstrap (Terraform state storage,
# resource groups and custom roles) stays, so the environment can be recreated; --all removes it too.
# Local mode only: a pipeline environment is torn down by its own workflow (scripts/destroy.sh).
#
# Usage: scripts/teardown-local.sh [--all]
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_root/.env"
# shellcheck source=scripts/lib/deploy-guard.sh
source "$repo_root/scripts/lib/deploy-guard.sh"
# shellcheck source=scripts/lib/env-file.sh
source "$repo_root/scripts/lib/env-file.sh"

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

what="platform and sign-in"
[[ "$all" == "true" ]] && what="platform, sign-in and bootstrap (everything)"
read -r -p "Remove the '${TF_VAR_environment:-dev}' environment's $what for '$TF_VAR_name_prefix'? Type the name prefix to confirm: " answer
[[ "$answer" == "$TF_VAR_name_prefix" ]] || { echo "Cancelled."; exit 1; }

fail() { echo "$*" >&2; exit 1; }

# Identity first: its configuration names the web app, which the platform's outputs still describe.
# Each step reads its state first and skips only a state confirmed empty: one that can't be read
# stops the teardown with .env unchanged, so a rerun can finish.
if [[ -n "${TF_STATE_STORAGE_ACCOUNT:-}" ]]; then
  identity_state="$(tf identity state list)" ||
    fail "Couldn't read the sign-in (identity) state, so nothing was removed. Check access and rerun."
  platform_state="$(tf platform state list)" ||
    fail "Couldn't read the platform state, so nothing was removed. Check access and rerun."
  if [[ -z "$identity_state$platform_state" ]]; then
    echo "The platform and sign-in are already removed."
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
  identity_state="$(tf identity state list)" && platform_state="$(tf platform state list)" ||
    fail "Couldn't confirm the platform and sign-in were removed. Rerun to check and finish."
  [[ -z "$identity_state$platform_state" ]] ||
    fail "Some resources remain after the destroy; rerun to finish: $(echo $identity_state $platform_state)"
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
# is deleted afterwards, storage included. A removal that stopped part-way left that local state behind:
# it's the newest record, so a rerun carries on from it rather than from Azure's older copy.
bootstrap_dir="$repo_root/infra/deployments/bootstrap"
if [[ -n "${TF_STATE_STORAGE_ACCOUNT:-}" ]]; then
  owns_state_locally || exit 1
  # Same handling as scripts/terraform.sh: empty values fall back to defaults, and Git Bash leaves ids alone.
  while IFS='=' read -r name _; do
    if [[ -z "${!name}" ]]; then unset "$name"; fi
  done < <(env | grep '^TF_VAR_' || true)
  export MSYS2_ENV_CONV_EXCL="${MSYS2_ENV_CONV_EXCL:+$MSYS2_ENV_CONV_EXCL;}TF_VAR_"

  cd "$bootstrap_dir"
  trap 'rm -f "$bootstrap_dir/backend_override.tf"' EXIT
  if [[ -s terraform.tfstate ]]; then
    echo "Resuming the bootstrap's removal from the state on this machine."
    cat > backend_override.tf <<'EOF'
terraform {
  backend "local" {}
}
EOF
    terraform init -input=false -reconfigure >/dev/null
  else
    terraform init -input=false -reconfigure \
      -backend-config="resource_group_name=$TF_STATE_RESOURCE_GROUP" \
      -backend-config="storage_account_name=$TF_STATE_STORAGE_ACCOUNT" \
      -backend-config="container_name=$TF_STATE_CONTAINER" \
      -backend-config="key=bootstrap.tfstate" >/dev/null
    cat > backend_override.tf <<'EOF'
terraform {
  backend "local" {}
}
EOF
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
fi

bootstrap_group="rg-$TF_VAR_name_prefix-bootstrap"
exists="$(az group exists --name "$bootstrap_group")" ||
  fail "Couldn't check whether $bootstrap_group still exists. .env is unchanged; rerun to finish."
case "${exists//[[:space:]]/}" in
  true) az group delete --name "$bootstrap_group" --yes ;;
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
