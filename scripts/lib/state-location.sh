#!/usr/bin/env bash
# Where each deployment's Terraform state lives in the state storage: one state file per deployment
# and environment. Identity state holds demo user passwords and the local sign-in secret, so it lives
# in a container no pipeline identity can read.

# Prints the container holding <deployment>'s state.
state_container() {
  if [[ "$1" == "identity" ]]; then
    echo "${TF_STATE_OPERATOR_CONTAINER:?Missing TF_STATE_OPERATOR_CONTAINER; rerun scripts/bootstrap.sh to record it.}"
  else
    echo "$TF_STATE_CONTAINER"
  fi
}

# Prints the name of <deployment>'s state file.
state_key() {
  if [[ "$1" == "bootstrap" ]]; then
    echo "bootstrap.tfstate"
  else
    echo "$TF_VAR_environment/$1.tfstate"
  fi
}
