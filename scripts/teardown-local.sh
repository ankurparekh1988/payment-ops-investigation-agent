#!/usr/bin/env bash
# Removes what scripts/setup-local.sh created, except the bootstrap (state storage and resource
# groups), so the environment can be recreated. Local mode only: a pipeline environment is torn
# down by its own workflow (scripts/destroy.sh).
#
# Usage: scripts/teardown-local.sh
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_root/.env"
# shellcheck source=scripts/lib/deploy-guard.sh
source "$repo_root/scripts/lib/deploy-guard.sh"

[[ -f "$env_file" ]] || { echo "No .env; nothing to remove." >&2; exit 1; }
set -a
# shellcheck disable=SC1090
source "$env_file"
set +a
local_mode || { echo "This .env isn't a local-mode environment; use scripts/destroy.sh for a pipeline one." >&2; exit 1; }

tf() { "$repo_root/scripts/terraform.sh" "$@"; }

read -r -p "Remove the '${TF_VAR_environment:-dev}' environment's platform and sign-in for '$TF_VAR_name_prefix'? Type the name prefix to confirm: " answer
[[ "$answer" == "$TF_VAR_name_prefix" ]] || { echo "Cancelled."; exit 1; }

# Identity first: its configuration names the web app, which the platform's outputs still describe.
TF_VAR_web_app_url="$(tf platform output -raw web_app_url)"
TF_VAR_app_identity_principal_id="$(tf platform output -raw app_identity_principal_id)"
export TF_VAR_web_app_url TF_VAR_app_identity_principal_id
tf identity destroy -auto-approve
tf platform destroy -auto-approve

# These named what was just deleted; setup fills them in again.
# shellcheck source=scripts/lib/env-file.sh
source "$repo_root/scripts/lib/env-file.sh"
for name in TF_VAR_eval_foundry_account_id TF_VAR_entra_client_id TF_VAR_restricted_group_id; do
  set_env_value "$env_file" "$name" ""
done

echo
echo "Removed. The bootstrap remains so you can run scripts/setup-local.sh again; see SETUP.md to remove it too."
