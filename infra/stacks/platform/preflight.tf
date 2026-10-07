# Model preflight: fail the plan if a manifest model isn't offered in the region at its pinned
# version and deployment type, is closed to new deployments, or retires within the buffer.
# Quota and capacity headroom are checked by the deployment pipeline immediately before apply,
# because quota is shared across deployments and depends on what already exists.

data "azapi_resource_action" "model_catalog" {
  type                   = "Microsoft.CognitiveServices/locations@2024-10-01"
  resource_id            = "/subscriptions/${data.azurerm_client_config.current.subscription_id}/providers/Microsoft.CognitiveServices/locations/${var.location}"
  action                 = "models"
  method                 = "GET"
  response_export_values = ["value"]
}

locals {
  closed_lifecycle_states = ["Deprecating", "Deprecated"]

  catalog = {
    for entry in data.azapi_resource_action.model_catalog.output.value :
    "${entry.model.name}@${entry.model.version}" => entry.model...
    if try(entry.kind, "") == "OpenAI"
  }

  retirement_cutoff = timeadd(plantimestamp(), "${var.model_retirement_buffer_days * 24}h")

  model_checks = {
    for name, d in var.model_deployments : name => {
      label     = "${d.model} ${d.version} (${d.sku})"
      entry     = try(local.catalog["${d.model}@${d.version}"][0], null)
      lifecycle = try(local.catalog["${d.model}@${d.version}"][0].lifecycleStatus, "Unknown")
      retires   = try(local.catalog["${d.model}@${d.version}"][0].deprecation.inference, null)
      sku       = try([for s in local.catalog["${d.model}@${d.version}"][0].skus : s if s.name == d.sku][0], null)
      offered   = try([for s in local.catalog["${d.model}@${d.version}"][0].skus : s.name], [])
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
      condition     = !contains(local.closed_lifecycle_states, each.value.lifecycle)
      error_message = "${each.key}: ${each.value.label} is ${each.value.lifecycle} and closed to new deployments. Choose a newer version in ai/manifest.yaml."
    }

    precondition {
      condition     = each.value.sku != null
      error_message = "${each.key}: ${each.value.label} isn't offered with that deployment type in ${var.location} (offered: ${join(", ", each.value.offered)})."
    }

    precondition {
      condition     = each.value.retires == null || timecmp(coalesce(each.value.retires, "9999-12-31T00:00:00Z"), local.retirement_cutoff) > 0
      error_message = "${each.key}: ${each.value.label} retires on ${coalesce(each.value.retires, "n/a")}, within ${var.model_retirement_buffer_days} days. Upgrade it in ai/manifest.yaml."
    }

    precondition {
      condition     = try(each.value.sku.deprecationDate, null) == null || timecmp(coalesce(try(each.value.sku.deprecationDate, null), "9999-12-31T00:00:00Z"), local.retirement_cutoff) > 0
      error_message = "${each.key}: the ${each.value.label} deployment type is deprecated on ${coalesce(try(each.value.sku.deprecationDate, null), "n/a")}, within ${var.model_retirement_buffer_days} days."
    }
  }
}
