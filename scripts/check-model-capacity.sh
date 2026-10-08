#!/usr/bin/env bash
# Checks, immediately before apply, that the model deployments in a saved plan fit the
# subscription's quota and the region's available capacity.
#
# Quota is shared by every deployment of the same model and deployment type, so demand is summed
# across deployments. Our existing deployments already count towards current usage, so their
# capacity is added back; otherwise every re-apply would count them twice. Quota doesn't guarantee
# capacity, so any new capacity is also checked against what the region can provide.
#
# Runs in the deployment pipeline. Usage: scripts/check-model-capacity.sh <deployment> <plan file>
# Requires: az (signed in), jq, ARM_SUBSCRIPTION_ID, TF_VAR_location
set -euo pipefail

deployment="$1"
plan_file="$2"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
api="https://management.azure.com/subscriptions/${ARM_SUBSCRIPTION_ID:?}/providers/Microsoft.CognitiveServices/locations/${TF_VAR_location:?}"
api_version="2024-10-01"

plan="$(terraform -chdir="$repo_root/infra/deployments/$deployment" show -json "$plan_file")"

# Model deployments as {sku, model, version, capacity}, capacity in thousands of tokens per minute.
read_deployments='[.. | objects | select(.type? == "azurerm_cognitive_deployment") | .values
  | {sku: .sku[0].name, model: .model[0].name, version: .model[0].version, capacity: .sku[0].capacity}]'
desired="$(jq "(.planned_values // {}) | $read_deployments" <<< "$plan")"
existing="$(jq "(.prior_state.values // {}) | $read_deployments" <<< "$plan")"

if [[ "$(jq 'length' <<< "$desired")" == "0" ]]; then
  echo "No model deployments in the plan."
  exit 0
fi

# Sums capacity per combination of the given fields, as {"sku|model": total}.
totals_by() {
  jq --argjson fields "$1" 'map({key: ([.[$fields[]]] | join("|")), capacity})
    | group_by(.key) | map({key: .[0].key, value: (map(.capacity) | add)}) | from_entries'
}

usages="$(az rest --method get --url "$api/usages?api-version=$api_version" --query value -o json)"
failures=0

# Quota: shared per deployment type and model, across versions.
desired_quota="$(totals_by '["sku","model"]' <<< "$desired")"
existing_quota="$(totals_by '["sku","model"]' <<< "$existing")"
for key in $(jq -r 'keys[]' <<< "$desired_quota"); do
  IFS='|' read -r sku model <<< "$key"
  demand="$(jq -r --arg k "$key" '.[$k]' <<< "$desired_quota")"
  ours="$(jq -r --arg k "$key" '.[$k] // 0' <<< "$existing_quota")"
  read -r limit current < <(jq -r --arg name "OpenAI.$sku.$model" \
    '[.[] | select(.name.value == $name)][0] | "\((.limit // 0) | floor) \((.currentValue // 0) | floor)"' <<< "$usages")
  headroom=$(( limit - current + ours ))

  if (( demand > headroom )); then
    echo "::error::OpenAI.$sku.$model needs ${demand}K TPM but only ${headroom}K is free (limit ${limit}K, in use ${current}K, of which ours ${ours}K)."
    failures=$((failures + 1))
  else
    echo "Quota OpenAI.$sku.$model: ${demand}K TPM requested, ${headroom}K available."
  fi
done

# Capacity: only new capacity needs the region to provide it.
desired_versions="$(totals_by '["sku","model","version"]' <<< "$desired")"
existing_versions="$(totals_by '["sku","model","version"]' <<< "$existing")"
for key in $(jq -r 'keys[]' <<< "$desired_versions"); do
  IFS='|' read -r sku model version <<< "$key"
  additional=$(( $(jq -r --arg k "$key" '.[$k]' <<< "$desired_versions") - $(jq -r --arg k "$key" '.[$k] // 0' <<< "$existing_versions") ))
  (( additional > 0 )) || continue

  available="$(az rest --method get \
    --url "$api/modelCapacities?api-version=$api_version&modelFormat=OpenAI&modelName=$model&modelVersion=$version" \
    --query "value[?properties.skuName=='$sku'] | [0].properties.availableCapacity" -o tsv)"
  available="${available:-0}"

  if (( additional > ${available%.*} )); then
    echo "::error::$model $version ($sku) needs ${additional}K TPM of new capacity but the region has ${available%.*}K available."
    failures=$((failures + 1))
  else
    echo "Capacity $model $version ($sku): ${additional}K TPM new, ${available%.*}K available."
  fi
done

exit "$failures"
