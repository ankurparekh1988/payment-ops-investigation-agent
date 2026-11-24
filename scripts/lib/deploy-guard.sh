#!/usr/bin/env bash
# Where an environment may be changed from. By default only the deployment pipeline applies it,
# after approval. An environment with no pipeline, created by scripts/setup-local.sh, is deployed
# from a workstation instead: PAYMENTOPS_DEPLOY_FROM=local in .env, and its state storage is marked
# deploy-from=local by the bootstrap.
#
# This guards against mistakes, not against an Owner determined to bypass it: Azure RBAC is the
# boundary. So it never trusts a shell variable alone where Azure can confirm the fact.

# Succeeds in local mode. Exits the calling script when local mode is asked for on an environment
# that has a pipeline, so a workstation can never apply what the pipeline owns.
local_mode() {
  [[ "${PAYMENTOPS_DEPLOY_FROM:-}" == "local" ]] || return 1
  if [[ -n "${TF_VAR_github_repository:-}" ]]; then
    echo "PAYMENTOPS_DEPLOY_FROM=local is set, but so is TF_VAR_github_repository: this environment is deployed by" >&2
    echo "its pipeline. Remove PAYMENTOPS_DEPLOY_FROM from .env, or use a separate .env without the GitHub values." >&2
    exit 1
  fi
}

# Succeeds for Terraform commands that only read. Everything else counts as a change, including
# commands not listed here and global options such as -chdir placed before the command.
reads_only() {
  case "${1:-}" in
    plan | show | output | validate | providers | version) return 0 ;;
    state) [[ "${2:-}" =~ ^(list|show|pull)$ ]] ;;
    *) return 1 ;;
  esac
}

# Succeeds when the state storage named in .env is marked as a local-mode environment's, read back
# from Azure. A pipeline environment's storage has no mark, so pointing local mode at it is refused,
# and so is anything that can't be confirmed.
owns_state_locally() {
  local mark
  if [[ -z "${TF_STATE_STORAGE_ACCOUNT:-}" || -z "${TF_STATE_RESOURCE_GROUP:-}" ]]; then
    echo "No state storage in .env to confirm this environment is deployed from here; run scripts/setup-local.sh." >&2
    return 1
  fi
  if ! mark="$(az storage account show --name "$TF_STATE_STORAGE_ACCOUNT" --resource-group "$TF_STATE_RESOURCE_GROUP" \
                 --query 'tags."deploy-from"' -o tsv 2>/dev/null)"; then
    echo "Couldn't read state storage '$TF_STATE_STORAGE_ACCOUNT' to confirm this environment is deployed from here." >&2
    echo "Nothing was changed. Check 'az login' and the TF_STATE_* values in .env." >&2
    return 1
  fi
  if [[ "${mark%$'\r'}" != "local" ]]; then
    echo "State storage '$TF_STATE_STORAGE_ACCOUNT' isn't marked deploy-from=local, so its environment is deployed by" >&2
    echo "a pipeline. Local mode changes only environments that scripts/setup-local.sh created." >&2
    return 1
  fi
}

# Succeeds inside a GitHub Actions job that signs in with GitHub's OIDC token, and holds Terraform to
# that credential: with the Azure CLI fallback off, these variables set on a workstation get no token.
in_pipeline() {
  [[ "${GITHUB_ACTIONS:-}" == "true" && "${ARM_USE_OIDC:-}" == "true" && -n "${ARM_CLIENT_ID:-}" &&
     -n "${ACTIONS_ID_TOKEN_REQUEST_URL:-}" && -n "${ACTIONS_ID_TOKEN_REQUEST_TOKEN:-}" ]] || return 1
  export ARM_USE_CLI=false
}

# Succeeds when 'terraform <args>' may run for the deployment from here.
# Usage: may_change_environment <deployment> <terraform arguments...>
may_change_environment() {
  local deployment="$1"
  shift
  reads_only "$@" && return 0

  if local_mode; then
    owns_state_locally
    return
  fi

  # The exceptions need rights no pipeline should hold: the bootstrap creates the pipeline's own
  # identities and state (Owner), and identity creates Entra app registrations, groups and users.
  if [[ "$deployment" == "bootstrap" ]]; then
    if [[ -z "${TF_VAR_github_repository:-}" ]]; then
      echo "Changing a pipeline environment's bootstrap without TF_VAR_github_repository would remove its pipeline" >&2
      echo "identities. Restore the TF_VAR_github_* values in .env." >&2
      return 1
    fi
    return 0
  fi
  [[ "$deployment" == "identity" ]] && return 0
  in_pipeline && return 0

  echo "'terraform $*' for '$deployment' runs only in the deployment pipeline. Use 'plan' locally," >&2
  echo "or see SETUP.md to create a separate environment you deploy from this machine." >&2
  return 1
}
