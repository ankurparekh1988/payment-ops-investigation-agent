#!/usr/bin/env bash
# Where an environment may be changed from. By default only the deployment pipeline applies it,
# after approval. An environment with no pipeline, created by scripts/setup-local.sh, is deployed
# from a workstation instead: PAYMENTOPS_DEPLOY_FROM=local in .env.

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

# Succeeds when 'terraform <args>' may run for the deployment from here.
# Usage: may_change_environment <deployment> <terraform arguments...>
may_change_environment() {
  local deployment="$1"
  shift
  local changes_environment=false
  case "${1:-}" in
    apply | destroy | import | taint | untaint | force-unlock) changes_environment=true ;;
    state) [[ "${2:-}" =~ ^(rm|mv|push|replace-provider)$ ]] && changes_environment=true ;;
  esac
  [[ "$changes_environment" == "true" ]] || return 0
  local_mode && return 0

  # The exceptions need rights no pipeline should hold: the bootstrap creates the pipeline's own
  # identities and state (Owner), and identity creates Entra app registrations, groups and users.
  [[ "$deployment" == "bootstrap" || "$deployment" == "identity" ]] && return 0
  [[ "${GITHUB_ACTIONS:-}" == "true" ]] && return 0

  echo "'terraform $*' for '$deployment' runs only in the deployment pipeline. Use 'plan' locally," >&2
  echo "or see SETUP.md to create a separate environment you deploy from this machine." >&2
  return 1
}
