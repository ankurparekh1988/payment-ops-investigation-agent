locals {
  # The AI release manifest is the single source of truth for which models are deployed.
  manifest = yamldecode(file("${path.root}/../../../ai/manifest.yaml"))
  models   = local.manifest.models

  chat_deployments = {
    for slot, d in local.models.chat.deployments : d.deployment => {
      model          = d.model
      version        = d.version
      sku            = d.deploymentType
      capacity       = d.capacityK
      content_filter = "strict"
    }
  }

  model_deployments = merge(local.chat_deployments, {
    (local.models.embedding.deployment) = {
      model          = local.models.embedding.model
      version        = local.models.embedding.version
      sku            = local.models.embedding.deploymentType
      capacity       = local.models.embedding.capacityK
      content_filter = null
    }
    # The judge reads candidate answers, including deliberately unsafe ones in safety evaluations,
    # so it uses Microsoft's default filter rather than the strict one.
    (local.models.judge.deployment) = {
      model          = local.models.judge.model
      version        = local.models.judge.version
      sku            = local.models.judge.deploymentType
      capacity       = local.models.judge.capacityK
      content_filter = "default"
    }
  })
}

module "platform" {
  source = "../../stacks/platform"

  location    = var.location
  name_prefix = var.name_prefix
  environment = var.environment

  model_deployments            = local.model_deployments
  active_chat_deployment       = local.models.chat.deployments[local.models.chat.active].deployment
  embedding_deployment         = local.models.embedding.deployment
  model_retirement_buffer_days = var.model_retirement_buffer_days

  app_service_sku          = var.app_service_sku
  dotnet_version           = var.dotnet_version
  storage_replication_type = var.storage_replication_type
  log_retention_days       = var.log_retention_days
  log_daily_quota_gb       = var.log_daily_quota_gb
  monthly_budget           = var.monthly_budget
  alert_email              = var.alert_email
  entra_client_id          = var.entra_client_id
  restricted_group_id      = var.restricted_group_id
}
