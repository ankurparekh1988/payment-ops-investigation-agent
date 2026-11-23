#!/usr/bin/env bash
# Runs scripts/setup-local.sh and scripts/teardown-local.sh end to end in a scratch copy of the
# repository, with az, terraform, dotnet and jq replaced by stubs: nothing reaches Azure. The stubs keep
# just enough state to behave like the real thing: which deployments hold resources, which resource
# groups exist, and the bootstrap's local state file.
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
bootstrap_dir="$root/infra/deployments/bootstrap"
export STUB_LOG="$log" STUB_SECRETS="$secrets" STUB_STATE="$work/state" STUB_GROUPS="$work/groups"
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
  "group exists --name "*)
    [[ -z "${STUB_FAIL_GROUP_EXISTS:-}" ]] || exit 1
    grep -qxF "$4" "$STUB_GROUPS" 2>/dev/null && echo true || echo false ;;
  "group delete --name "*) grep -vxF "$4" "$STUB_GROUPS" > "$STUB_GROUPS.new"; mv "$STUB_GROUPS.new" "$STUB_GROUPS" ;;
  "storage account show "*)
    [[ -z "${STUB_FAIL_OWNER:-}" ]] || exit 1
    printf '%s\r\n' "${STUB_OWNER-local}" ;;
esac
EOF
cat > "$work/bin/terraform" <<'EOF'
#!/usr/bin/env bash
dir="$(basename "$PWD")"
override=no; [[ -f backend_override.tf ]] && override=yes
echo "terraform $dir $* eval_foundry=${TF_VAR_eval_foundry_account_id:-} override=$override" >> "$STUB_LOG"
mkdir -p "$STUB_STATE"
# STUB_FAIL_APPLY_ONCE names a marker file: the first platform apply fails, as a new custom role not yet visible would.
if [[ -n "${STUB_FAIL_APPLY_ONCE:-}" && "$dir" == platform && "$1" == apply && ! -f "$STUB_FAIL_APPLY_ONCE" ]]; then
  touch "$STUB_FAIL_APPLY_ONCE"; exit 1
fi
if [[ "$dir" == bootstrap ]]; then
  case "$1" in
    apply)
      # Applying with local state and none yet creates the bootstrap: count it, to catch duplicates.
      if [[ "$override" == yes && ! -s terraform.tfstate ]]; then
        echo created >> "$STUB_STATE/bootstrap-creations"
        echo "rg-forktest-bootstrap" >> "$STUB_GROUPS"
      fi
      [[ "$override" == yes ]] && echo local-state > terraform.tfstate
      ;;
    init) if [[ " $* " == *" -migrate-state "* ]]; then
            [[ -z "${STUB_FAIL_MIGRATE:-}" ]] || exit 1
            [[ "$override" == yes ]] && echo migrated-state > terraform.tfstate
          fi ;;
    destroy) [[ -z "${STUB_FAIL_BOOTSTRAP_DESTROY:-}" ]] || exit 1 ;;
  esac
fi
if [[ "$dir" == platform || "$dir" == identity ]]; then
  case "$1" in
    apply) touch "$STUB_STATE/$dir" ;;
    destroy) [[ "${STUB_DESTROY_LEAVES:-}" == "$dir" ]] || rm -f "$STUB_STATE/$dir" ;;
  esac
fi
case "$*" in
  "state list")
    [[ "${STUB_FAIL_STATE_LIST:-}" != "$dir" ]] || exit 1
    if [[ "$dir" == bootstrap ]]; then
      printf '%s\n' module.bootstrap.azurerm_resource_group.bootstrap module.bootstrap.module.state.azurerm_storage_account.this \
        module.bootstrap.module.state.azurerm_storage_container.this module.bootstrap.module.state.azurerm_storage_container.operator \
        module.bootstrap.azurerm_resource_group.workload
    elif [[ -f "$STUB_STATE/$dir" ]]; then
      echo "module.$dir.azurerm_resource.this"
    fi ;;
  "output -raw state_resource_group_name") echo rg-forktest-bootstrap ;;
  "output -raw state_storage_account_name") echo sttfstate ;;
  "output -raw state_container_name") echo tfstate ;;
  "output -raw operator_state_container_name") echo operator ;;
  "output -raw foundry_account_id") echo "/subscriptions/x/foundry-${STUB_RUN:-1}" ;;
  "output -raw search_service_id") [[ -f "$STUB_STATE/platform" ]] && echo search || exit 1 ;;
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
# The scripts wait between retries; the test doesn't.
printf '#!/usr/bin/env bash\nexit 0\n' > "$work/bin/sleep"
chmod +x "$work/bin/"*
export PATH="$work/bin:$PATH"

# Starts a scenario from nothing: a new .env, no Azure resources, no local state.
fresh() {
  cat > "$root/.env" <<'EOF'
PAYMENTOPS_DEPLOY_FROM=local
TF_VAR_location=canadacentral
TF_VAR_name_prefix=forktest
TF_VAR_alert_email=ops@example.com
EOF
  rm -rf "$work/state" "$work/groups" "$bootstrap_dir/terraform.tfstate" "$bootstrap_dir/backend_override.tf"
  : > "$log"
  : > "$secrets"
}
setup() { bash "$root/scripts/setup-local.sh" 2>&1; }
teardown() { bash "$root/scripts/teardown-local.sh" "$@" <<< "forktest" 2>&1; }
env_has() { grep -q "^$1=.\+" "$root/.env"; }
creations() { [[ -f "$work/state/bootstrap-creations" ]] && wc -l < "$work/state/bootstrap-creations" | tr -d ' ' || echo 0; }

# --- First run ---------------------------------------------------------------------------------
fresh
out="$(STUB_RUN=1 setup)"
status=$?
check "setup completes" test "$status" -eq 0
[[ "$status" -eq 0 ]] || echo "$out" | tail -12
check "bootstrap applies without a prompt" grep -q "terraform bootstrap apply -auto-approve" "$log"
check "platform applies the checked plan" grep -q "terraform platform apply -input=false platform.tfplan" "$log"
check "capacity is checked before each platform apply" \
  bash -c "grep -E '^(capacity|terraform platform apply)' '$log' | paste -sd' ' | grep -q '^capacity platform platform.tfplan terraform platform apply .* capacity platform platform.tfplan terraform platform apply'"
check "second bootstrap gets the new Foundry account" grep -q "terraform bootstrap apply -auto-approve eval_foundry=/subscriptions/x/foundry-1" "$log"
check "the second bootstrap confirms the state storage is local before applying" \
  bash -c "grep -E '^az storage account show|^terraform bootstrap apply' '$log' | grep -B1 'bootstrap apply .*foundry-1' | head -1 | grep -q '^az storage account show'"
check "operator recorded as a developer" grep -q "TF_VAR_developer_object_ids='\[\"33333333-3333-3333-3333-333333333333\"\]'" "$root/.env"
check "identity applies without a prompt" grep -q "terraform identity apply -auto-approve" "$log"
check "client id written" grep -qx "AzureAd:ClientId=client-1" "$secrets"
check "search endpoint written" grep -qx "PaymentOps:Search:Endpoint=https://search.example" "$secrets"
check "secret written through stdin" grep -q "\"AzureAd:ClientCredentials:0:ClientSecret\": \"$SECRET\"" "$secrets"
check "secret never printed" bash -c "! grep -q '$SECRET' <<< \"\$1\"" _ "$out"
check "secret never an argument" bash -c "! grep -q '$SECRET' '$log'"
check "the bootstrap's local state is gone once it's in Azure" test ! -e "$bootstrap_dir/terraform.tfstate"

# --- A platform apply that fails once is retried ------------------------------------------------
: > "$log"
STUB_FAIL_APPLY_ONCE="$work/failed-once" SETUP_RETRY_SECONDS=0 STUB_RUN=1 setup > /dev/null
check "setup retries a failed platform apply" test $? -eq 0
check "the retry plans again before applying" bash -c "[[ \$(grep -c 'terraform platform plan' '$log') -ge 3 ]]"

# --- Local mode refuses state storage that isn't marked as its own ------------------------------
: > "$log"
STUB_OWNER="" STUB_RUN=1 setup > /dev/null
check "setup refuses state storage not marked deploy-from=local" test $? -ne 0
check "nothing applied on unmarked state" bash -c "! grep -q ' apply' '$log'"

# --- Teardown, then a second setup: no stale ids survive ----------------------------------------
: > "$log"
teardown > /dev/null
check "teardown completes" test $? -eq 0
check "teardown destroys identity, then the platform" \
  bash -c "grep -E '^terraform (identity|platform) destroy' '$log' | awk '{print \$2}' | paste -sd' ' | grep -qx 'identity platform'"
check "teardown forgets the Foundry account" bash -c "! grep -q '^TF_VAR_eval_foundry_account_id=.\+' '$root/.env'"
check "teardown forgets the sign-in ids" bash -c "! grep -q '^TF_VAR_entra_client_id=.\+' '$root/.env'"
out="$(teardown)"
check "a second teardown finds nothing left" grep -q "already removed" <<< "$out"

: > "$log"
STUB_RUN=2 setup > /dev/null
check "second setup completes" test $? -eq 0
check "first bootstrap of the second setup names no deleted account" \
  bash -c "grep -m1 'terraform bootstrap apply' '$log' | grep -q 'eval_foundry= '"
check "second bootstrap uses the new account" grep -q "terraform bootstrap apply -auto-approve eval_foundry=/subscriptions/x/foundry-2" "$log"

# --- A failed read stops setup rather than writing an empty setting ------------------------------
: > "$secrets"
STUB_FAIL_GROUP=1 STUB_RUN=2 setup > /dev/null
check "a failed output read stops setup" test $? -ne 0
check "no empty setting is written" bash -c "! grep -q '^PaymentOps:Authorization:RestrictedGroupId=' '$secrets'"

# --- Teardown fails closed: an unreadable state is never taken for an empty one -----------------
: > "$log"
out="$(STUB_FAIL_STATE_LIST=platform teardown --all)"
check "an unreadable platform state stops teardown" test $? -ne 0
check "it doesn't report the platform as removed" bash -c "! grep -q 'already removed' <<< \"\$1\"" _ "$out"
check "nothing is destroyed" bash -c "! grep -qE ' destroy|^az group delete' '$log'"
check ".env keeps the state storage" env_has TF_STATE_STORAGE_ACCOUNT
check ".env keeps the sign-in ids" env_has TF_VAR_entra_client_id

: > "$log"
STUB_DESTROY_LEAVES=platform teardown --all > /dev/null
check "resources left after a destroy stop --all" test $? -ne 0
check "the bootstrap isn't touched while the platform remains" bash -c "! grep -qE '^terraform bootstrap|^az group delete' '$log'"
check ".env keeps the state storage after the failed destroy" env_has TF_STATE_STORAGE_ACCOUNT

# --- An interrupted --all keeps what a rerun needs, and the rerun finishes ----------------------
: > "$log"
STUB_FAIL_BOOTSTRAP_DESTROY=1 teardown --all > /dev/null
check "a failed bootstrap destroy stops --all" test $? -ne 0
check "the bootstrap's state is kept on this machine" test -s "$bootstrap_dir/terraform.tfstate"
check ".env keeps the state storage after the interruption" env_has TF_STATE_STORAGE_ACCOUNT
check "the bootstrap resource group isn't deleted" grep -qx rg-forktest-bootstrap "$work/groups"
out="$(STUB_RUN=3 setup)"
check "setup refuses while a removal is unfinished" test $? -ne 0
check "and says how to finish it" grep -q "teardown-local.sh --all" <<< "$out"

: > "$log"
STUB_FAIL_GROUP_EXISTS=1 teardown --all > /dev/null
check "--all fails when it can't check the bootstrap resource group" test $? -ne 0
check ".env keeps the state storage when the group can't be checked" env_has TF_STATE_STORAGE_ACCOUNT

: > "$log"
teardown --all > /dev/null
check "the rerun of --all completes" test $? -eq 0
check "the rerun resumes from the local state rather than Azure's copy" bash -c "! grep -q -- '-migrate-state' '$log'"
check "the bootstrap resource group is deleted" bash -c "! grep -qx rg-forktest-bootstrap '$work/groups'"
check "--all forgets the state storage" bash -c "! grep -q '^TF_STATE_STORAGE_ACCOUNT=.\+' '$root/.env'"
check "--all leaves no local state behind" \
  bash -c "! ls '$bootstrap_dir/backend_override.tf' '$bootstrap_dir/terraform.tfstate' 2>/dev/null | grep -q ."

# --- Removing everything in one go ---------------------------------------------------------------
fresh
STUB_RUN=1 setup > /dev/null
: > "$log"
teardown --all > /dev/null
check "teardown --all completes" test $? -eq 0
check "--all removes identity, platform, bootstrap, then the state's resource group" \
  bash -c "grep -E '^terraform (identity|platform|bootstrap) destroy|^az group delete' '$log' | awk '{print \$2}' | paste -sd' ' | grep -qx 'identity platform bootstrap group'"
check "--all deletes the bootstrap resource group" grep -q "^az group delete --name rg-forktest-bootstrap --yes" "$log"
check "--all leaves the protected state storage to the group delete" \
  grep -q "terraform bootstrap state rm module.bootstrap.module.state.azurerm_storage_account.this" "$log"
check "--all keeps the workload group in Terraform's destroy" bash -c "! grep -q 'state rm.*azurerm_resource_group.workload' '$log'"
out="$(STUB_RUN=4 setup)"
check "setup after --all starts from scratch" grep -q "First run: creating state storage" <<< "$out"

# --- A state migration that fails is resumed, never restarted -----------------------------------
fresh
STUB_FAIL_MIGRATE=1 STUB_RUN=1 setup > /dev/null
check "a failed state migration stops setup" test $? -ne 0
check "the bootstrap was created once" test "$(creations)" -eq 1
check "its state is kept on this machine" test -s "$bootstrap_dir/terraform.tfstate"
check ".env names no state storage until the state is in it" bash -c "! grep -q '^TF_STATE_STORAGE_ACCOUNT=.\+' '$root/.env'"
: > "$log"
out="$(STUB_RUN=1 setup)"
check "the rerun completes" test $? -eq 0
check "the rerun says it's resuming" grep -q "Resuming" <<< "$out"
check "the rerun applies against the kept local state first" \
  bash -c "grep -m1 'terraform bootstrap apply' '$log' | grep -q 'override=yes'"
check "nothing is created twice" test "$(creations)" -eq 1
check "the state is moved into Azure" grep -q -- "-migrate-state" "$log"
check ".env names the state storage once it holds the state" env_has TF_STATE_STORAGE_ACCOUNT
check "the local copy is gone after the move" test ! -e "$bootstrap_dir/terraform.tfstate"

# --- A bootstrap that exists without any state isn't created again ------------------------------
fresh
echo rg-forktest-bootstrap > "$work/groups"
out="$(STUB_RUN=1 setup)"
check "setup refuses when the bootstrap exists but no state names it" test $? -ne 0
check "the refusal names the resource group" grep -q "rg-forktest-bootstrap" <<< "$out"
check "nothing applied over the existing bootstrap" bash -c "! grep -q ' apply' '$log'"
fresh
STUB_FAIL_GROUP_EXISTS=1 STUB_RUN=1 setup > /dev/null
check "setup refuses when it can't check for an existing bootstrap" test $? -ne 0
check "nothing applied when the check fails" bash -c "! grep -q ' apply' '$log'"

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
fresh
out="$(STUB_FREE_SEARCH=other-free STUB_RUN=3 setup)"
check "an existing free Search service stops setup" test $? -ne 0
check "the message names the other service" grep -q "other-free" <<< "$out"
check "nothing applied after the refusal" bash -c "! grep -q 'apply' '$log'"

if (( failures )); then
  echo "$failures failed"
  exit 1
fi
echo "all passed"
