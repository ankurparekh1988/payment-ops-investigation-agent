#!/usr/bin/env bash
# Creates or updates the Entra ID objects for one environment: the app registration with its app
# roles, the Risk and Compliance group and, optionally, demo users. Needs directory rights no
# pipeline holds, so it's run by a person. Records the values the platform needs in .env.
#
# Usage: scripts/identity.sh   (run after the platform is deployed; then scripts/configure-github.sh)
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_root/.env"
# shellcheck source=scripts/lib/env-file.sh
source "$repo_root/scripts/lib/env-file.sh"

# Users are sent back to the deployed web app, and the app's managed identity redeems their sign-in.
TF_VAR_web_app_url="$("$repo_root/scripts/terraform.sh" platform output -raw web_app_url)"
TF_VAR_app_identity_principal_id="$("$repo_root/scripts/terraform.sh" platform output -raw app_identity_principal_id)"
export TF_VAR_web_app_url TF_VAR_app_identity_principal_id

"$repo_root/scripts/terraform.sh" identity apply

set_env_value "$env_file" TF_VAR_entra_client_id "$("$repo_root/scripts/terraform.sh" identity output -raw client_id)"
set_env_value "$env_file" TF_VAR_restricted_group_id "$("$repo_root/scripts/terraform.sh" identity output -raw restricted_group_id)"

echo
echo "Recorded the client ID and Restricted group in .env. Next: scripts/configure-github.sh"
echo "Demo user sign-ins: scripts/terraform.sh identity output -json demo_user_sign_ins"
echo "Local sign-in: see 'Running locally with sign-in' in docs/Deployment.md"
