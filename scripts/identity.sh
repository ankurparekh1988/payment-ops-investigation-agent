#!/usr/bin/env bash
# Creates or updates the Entra ID objects for one environment: the app registration with its app
# roles, the Risk and Compliance group and, optionally, demo users. Needs directory rights no
# pipeline holds, so it's run by a person. Records the values the platform needs in .env.
#
# Usage: scripts/identity.sh   (run after the platform is deployed; then scripts/configure-github.sh)
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_root/.env"

# Writes or replaces KEY=value in .env.
set_env_value() {
  local key="$1" value="$2"
  if grep -q "^${key}=" "$env_file"; then
    sed -i.bak "s|^${key}=.*|${key}=${value}|" "$env_file" && rm -f "$env_file.bak"
  else
    printf '%s=%s\n' "$key" "$value" >> "$env_file"
  fi
}

"$repo_root/scripts/terraform.sh" identity apply

set_env_value TF_VAR_entra_client_id "$("$repo_root/scripts/terraform.sh" identity output -raw client_id)"
set_env_value TF_VAR_restricted_group_id "$("$repo_root/scripts/terraform.sh" identity output -raw restricted_group_id)"

echo
echo "Recorded the client ID and Restricted group in .env. Next: scripts/configure-github.sh"
echo "Demo user sign-ins: scripts/terraform.sh identity output -json demo_user_sign_ins"
