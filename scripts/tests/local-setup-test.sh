#!/usr/bin/env bash
# Runs scripts/setup-local.sh and scripts/teardown-local.sh end to end in a scratch copy of the
# repository, with az, terraform, dotnet and jq replaced by stubs: nothing reaches Azure.
# Usage: bash scripts/tests/local-setup-test.sh
set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
failures=0
check() {
  local description="$1"
  shift
  if "$@"; then echo "ok   $description"; else echo "FAIL $description"; failures=$((failures + 1)); fi
}

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
root="$work/repo"
mkdir -p "$root" "$work/bin"
cp -r "$repo_root/scripts" "$root/"
mkdir -p "$root/infra/deployments/bootstrap" "$root/infra/deployments/platform" "$root/infra/deployments/identity" \
         "$root/src/PaymentOps.Web" "$root/tools" "$root/data/knowledge/src" "$root/ai"
# The capacity check has its own CI coverage; here it only has to be called in order.
printf '#!/usr/bin/env bash\necho "capacity $*" >> "$STUB_LOG"\n' > "$root/scripts/check-model-capacity.sh"
log="$work/calls.log"
secrets="$work/user-secrets.log"
export STUB_LOG="$log" STUB_SECRETS="$secrets"
SECRET="s3cret-never-printed"
export STUB_SECRET="$SECRET"

cat > "$work/bin/az" <<'EOF'
#!/usr/bin/env bash
echo "az $*" >> "$STUB_LOG"
case "$*" in
  "account show --query id -o tsv") echo "11111111-1111-1111-1111-111111111111" ;;
  "account show --query tenantId -o tsv") echo "22222222-2222-2222-2222-222222222222" ;;
  "ad signed-in-user show --query id -o tsv") echo "33333333-3333-3333-3333-333333333333" ;;
  rest*searchServices*) printf '%s\n' "${STUB_FREE_SEARCH:-}" ;;
esac
EOF
cat > "$work/bin/terraform" <<'EOF'
#!/usr/bin/env bash
dir="$(basename "$PWD")"
echo "terraform $dir $* eval_foundry=${TF_VAR_eval_foundry_account_id:-}" >> "$STUB_LOG"
# STUB_FAIL_APPLY_ONCE names a marker file: the first platform apply fails, as a new custom role not yet visible would.
if [[ -n "${STUB_FAIL_APPLY_ONCE:-}" && "$dir" == platform && "$1" == apply && ! -f "$STUB_FAIL_APPLY_ONCE" ]]; then
  touch "$STUB_FAIL_APPLY_ONCE"; exit 1
fi
case "$*" in
  "output -raw state_resource_group_name") echo rg-state ;;
  "output -raw state_storage_account_name") echo sttfstate ;;
  "output -raw state_container_name") echo tfstate ;;
  "output -raw operator_state_container_name") echo operator ;;
  "output -raw foundry_account_id") echo "/subscriptions/x/foundry-${STUB_RUN:-1}" ;;
  "output -raw search_service_id") [[ -n "${STUB_PLATFORM_EXISTS:-}" ]] && echo search || exit 1 ;;
  "output -raw search_endpoint") echo https://search.example ;;
  "output -raw foundry_endpoint") echo https://foundry.example/openai/v1 ;;
  "output -raw content_safety_endpoint") echo https://foundry.example ;;
  "output -raw web_app_url") echo https://app.example ;;
  "output -raw app_identity_principal_id") echo app-principal ;;
  "output -raw client_id") echo "client-${STUB_RUN:-1}" ;;
  "output -raw restricted_group_id") [[ -z "${STUB_FAIL_GROUP:-}" ]] && echo restricted-group || exit 1 ;;
  "output -json local_client_secret") printf '"%s"\n' "$STUB_SECRET" ;;
  output) ;;
  output*) exit 1 ;;
esac
exit 0
EOF
cat > "$work/bin/dotnet" <<'EOF'
#!/usr/bin/env bash
echo "dotnet $*" >> "$STUB_LOG"
if [[ "$1" == "user-secrets" && " $* " == *" set "* ]]; then
  if [[ "${@: -1}" == "set" ]]; then cat >> "$STUB_SECRETS"; echo >> "$STUB_SECRETS"; else echo "${*: -2:1}=${*: -1}" >> "$STUB_SECRETS"; fi
fi
exit 0
EOF
# Stands in for jq only where the scripts use it: wrapping the secret's JSON string in an object.
cat > "$work/bin/jq" <<'EOF'
#!/usr/bin/env bash
printf '{"AzureAd:ClientCredentials:0:ClientSecret": %s}' "$(cat)"
EOF
chmod +x "$work/bin/"*
export PATH="$work/bin:$PATH"

cat > "$root/.env" <<'EOF'
PAYMENTOPS_DEPLOY_FROM=local
TF_VAR_location=canadacentral
TF_VAR_name_prefix=forktest
TF_VAR_alert_email=ops@example.com
EOF

# --- First run ---------------------------------------------------------------------------------
out="$(STUB_RUN=1 bash "$root/scripts/setup-local.sh" 2>&1)"
status=$?
check "setup completes" test "$status" -eq 0
[[ "$status" -eq 0 ]] || echo "$out" | tail -12
check "bootstrap applies without a prompt" grep -q "terraform bootstrap apply -auto-approve" "$log"
check "platform applies the checked plan" grep -q "terraform platform apply -input=false platform.tfplan" "$log"
check "capacity is checked before each platform apply" \
  bash -c "grep -E '^(capacity|terraform platform apply)' '$log' | paste -sd' ' | grep -q '^capacity platform platform.tfplan terraform platform apply .* capacity platform platform.tfplan terraform platform apply'"
check "second bootstrap gets the new Foundry account" grep -q "terraform bootstrap apply -auto-approve eval_foundry=/subscriptions/x/foundry-1" "$log"
check "operator recorded as a developer" grep -q "TF_VAR_developer_object_ids='\[\"33333333-3333-3333-3333-333333333333\"\]'" "$root/.env"
check "identity applies without a prompt" grep -q "terraform identity apply -auto-approve" "$log"
check "client id written" grep -qx "AzureAd:ClientId=client-1" "$secrets"
check "search endpoint written" grep -qx "PaymentOps:Search:Endpoint=https://search.example" "$secrets"
check "secret written through stdin" grep -q "\"AzureAd:ClientCredentials:0:ClientSecret\": \"$SECRET\"" "$secrets"
check "secret never printed" bash -c "! grep -q '$SECRET' <<< \"\$1\"" _ "$out"
check "secret never an argument" bash -c "! grep -q '$SECRET' '$log'"

# --- A platform apply that fails once is retried ------------------------------------------------
: > "$log"
STUB_FAIL_APPLY_ONCE="$work/failed-once" SETUP_RETRY_SECONDS=0 STUB_RUN=1 bash "$root/scripts/setup-local.sh" > /dev/null 2>&1
check "setup retries a failed platform apply" test $? -eq 0
check "the retry plans again before applying"   bash -c "[[ \$(grep -c 'terraform platform plan' '$log') -ge 3 ]]"

# --- Teardown, then a second setup: no stale ids survive ----------------------------------------
: > "$log"
STUB_PLATFORM_EXISTS=1 bash "$root/scripts/teardown-local.sh" <<< "forktest" > /dev/null 2>&1
check "teardown destroys identity, then the platform" \
  bash -c "grep -E '^terraform (identity|platform) destroy' '$log' | awk '{print \$2}' | paste -sd' ' | grep -qx 'identity platform'"
check "teardown forgets the Foundry account" bash -c "! grep -q '^TF_VAR_eval_foundry_account_id=.\+' '$root/.env'"
check "teardown forgets the sign-in ids" bash -c "! grep -q '^TF_VAR_entra_client_id=.\+' '$root/.env'"

: > "$log"
out="$(STUB_RUN=2 bash "$root/scripts/setup-local.sh" 2>&1)"
check "second setup completes" test $? -eq 0
check "first bootstrap of the second setup names no deleted account" \
  bash -c "grep -m1 'terraform bootstrap apply' '$log' | grep -q 'eval_foundry=\$'"
check "second bootstrap uses the new account" grep -q "terraform bootstrap apply -auto-approve eval_foundry=/subscriptions/x/foundry-2" "$log"

# --- A failed read stops setup rather than writing an empty setting ------------------------------
: > "$secrets"
STUB_FAIL_GROUP=1 STUB_PLATFORM_EXISTS=1 STUB_RUN=2 bash "$root/scripts/setup-local.sh" > /dev/null 2>&1
check "a failed output read stops setup" test $? -ne 0
check "no empty setting is written" bash -c "! grep -q '^PaymentOps:Authorization:RestrictedGroupId=' '$secrets'"

# --- jq's Windows line endings are stripped -----------------------------------------------------
mkdir -p "$work/crlf"
cat > "$work/crlf/jq" <<'EOF'
#!/usr/bin/env bash
printf 'value\r\n'
EOF
chmod +x "$work/crlf/jq"
check "jq output has no carriage return" \
  bash -c 'PATH="$1:$PATH"; source "$2"; read -r value < <(jq .); [[ "$value" == "value" ]]' _ "$work/crlf" "$root/scripts/lib/jq.sh"

check "the capacity check uses the jq wrapper" grep -q 'source "$repo_root/scripts/lib/jq.sh"' "$repo_root/scripts/check-model-capacity.sh"

# --- Refusals before anything is created --------------------------------------------------------
: > "$log"
out="$(STUB_FREE_SEARCH=other-free STUB_RUN=3 bash "$root/scripts/setup-local.sh" 2>&1)"
check "an existing free Search service stops setup" test $? -ne 0
check "the message names the other service" grep -q "other-free" <<< "$out"
check "nothing applied after the refusal" bash -c "! grep -q 'apply' '$log'"

if (( failures )); then
  echo "$failures failed"
  exit 1
fi
echo "all passed"
