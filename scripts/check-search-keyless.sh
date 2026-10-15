#!/usr/bin/env bash
# Proves the search service works keyless: local authentication is off, anonymous requests are
# refused, and the signed-in identity can create, write, read and delete an index with Entra alone.
#
# Usage: scripts/check-search-keyless.sh <resource-group> <search-endpoint>
set -euo pipefail

resource_group="${1:?Usage: scripts/check-search-keyless.sh <resource-group> <search-endpoint>}"
endpoint="${2:?Usage: scripts/check-search-keyless.sh <resource-group> <search-endpoint>}"
service="${endpoint#https://}"
service="${service%%.*}"
index="keyless-check"
api="api-version=2025-09-01"
index_url="$endpoint/indexes/$index?$api"
docs_url="$endpoint/indexes/$index/docs"

search() { az rest --resource https://search.azure.com --only-show-errors "$@"; }

fail_with_role_hint() {
  echo "::error::$1 failed. If the response was 403, the deploy identity lacks a Search role: re-run scripts/bootstrap.sh."
  exit 1
}

disabled="$(az resource show --resource-group "$resource_group" --name "$service" \
  --resource-type Microsoft.Search/searchServices --query properties.disableLocalAuth --output tsv)"
if [[ "$disabled" != "true" ]]; then
  echo "::error::Local authentication is enabled on $service."
  exit 1
fi
echo "Local authentication is disabled."

anonymous="$(curl --silent --output /dev/null --write-out '%{http_code}' --max-time 30 "$endpoint/indexes?$api" || true)"
if [[ "$anonymous" != "401" && "$anonymous" != "403" ]]; then
  echo "::error::An anonymous request returned $anonymous instead of 401 or 403."
  exit 1
fi
echo "Anonymous requests are refused ($anonymous)."

# A crashed earlier run may have left the index behind, and the free tier allows only three.
search --method delete --url "$index_url" >/dev/null 2>&1 || true
trap 'search --method delete --url "$index_url" >/dev/null 2>&1 || true' EXIT

search --method put --url "$index_url" --body '{
  "name": "keyless-check",
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true },
    { "name": "content", "type": "Edm.String", "searchable": true }
  ]
}' >/dev/null || fail_with_role_hint "Creating an index (Search Service Contributor)"
echo "Created the index."

search --method post --url "$docs_url/index?$api" \
  --body '{ "value": [ { "@search.action": "upload", "id": "1", "content": "keyless" } ] }' >/dev/null \
  || fail_with_role_hint "Uploading a document (Search Index Data Contributor)"
echo "Uploaded a document."

# A write can take a moment to become readable.
for attempt in $(seq 1 10); do
  content="$(search --method get --url "$docs_url/1?$api" --query content --output tsv 2>/dev/null || true)"
  if [[ "$content" == "keyless" ]]; then
    echo "Read the document back."
    break
  fi
  if [[ "$attempt" == 10 ]]; then
    fail_with_role_hint "Reading the document back (Search Index Data Contributor)"
  fi
  sleep 2
done

search --method post --url "$docs_url/index?$api" \
  --body '{ "value": [ { "@search.action": "delete", "id": "1" } ] }' >/dev/null \
  || fail_with_role_hint "Deleting the document (Search Index Data Contributor)"
echo "Deleted the document."

search --method delete --url "$index_url" >/dev/null || fail_with_role_hint "Deleting the index (Search Service Contributor)"
trap - EXIT
echo "Deleted the index. The search service works keyless."
