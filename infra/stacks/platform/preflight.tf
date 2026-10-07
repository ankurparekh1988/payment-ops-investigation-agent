# Model preflight: fail the plan, before anything is applied, if a manifest model isn't available
# in the region at its pinned version and deployment type, retires too soon, or exceeds quota.

data "azapi_resource_action" "model_catalog" {
  type                   = "Microsoft.CognitiveServices/locations@2024-10-01"
  resource_id            = "/subscriptions/${data.azurerm_client_config.current.subscription_id}/providers/Microsoft.CognitiveServices/locations/${var.location}"
  action                 = "models"
  method                 = "GET"
  response_export_values = ["value"]
}

data "azapi_resource_action" "model_quota" {
  type                   = "Microsoft.CognitiveServices/locations@2024-10-01"
  resource_id            = "/subscriptions/${data.azurerm_client_config.current.subscription_id}/providers/Microsoft.CognitiveServices/locations/${var.location}"
  action                 = "usages"
  method                 = "GET"
  response_export_values = ["value"]
}

locals {
  catalog = {
    for entry in data.azapi_resource_action.model_catalog.output.value :
    "${entry.model.name}@${entry.model.version}" => entry.model...
    if try(entry.kind, "") == "OpenAI"
  }

  quota_limits = {
    for usage in data.azapi_resource_action.model_quota.output.value :
    usage.name.value => usage.limit
  }

  retirement_cutoff = timeadd(plantimestamp(), "${var.model_retirement_buffer_days * 24}h")

  model_checks = {
    for name, d in var.model_deployments : name => {
      label     = "${d.model} ${d.version} (${d.sku})"
      entry     = try(local.catalog["${d.model}@${d.version}"][0], null)
      skus      = try([for sku in local.catalog["${d.model}@${d.version}"][0].skus : sku.name], [])
      retires   = try(local.catalog["${d.model}@${d.version}"][0].deprecation.inference, null)
      capacity  = d.capacity
      quota     = try(local.quota_limits["OpenAI.${d.sku}.${d.model}"], 0)
      requested = d.sku
    }
  }
}

resource "terraform_data" "model_preflight" {
  for_each = local.model_checks

  input = each.value.label

  lifecycle {
    precondition {
      condition     = each.value.entry != null
      error_message = "${each.key}: ${each.value.label} isn't available in ${var.location}. Check the version in ai/manifest.yaml."
    }

    precondition {
      condition     = contains(each.value.skus, each.value.requested)
      error_message = "${each.key}: ${each.value.label} isn't offered as ${each.value.requested} in ${var.location} (offered: ${join(", ", each.value.skus)})."
    }

    precondition {
      condition     = each.value.retires == null || timecmp(coalesce(each.value.retires, "9999-12-31T00:00:00Z"), local.retirement_cutoff) > 0
      error_message = "${each.key}: ${each.value.label} retires on ${coalesce(each.value.retires, "n/a")}, within ${var.model_retirement_buffer_days} days. Upgrade it in ai/manifest.yaml."
    }

    precondition {
      condition     = each.value.capacity <= each.value.quota
      error_message = "${each.key}: requests ${each.value.capacity}K TPM but the subscription's quota for ${each.value.label} is ${each.value.quota}K TPM."
    }
  }
}
