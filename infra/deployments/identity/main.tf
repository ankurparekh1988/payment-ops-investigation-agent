module "identity" {
  source = "../../stacks/identity"

  name_prefix                = var.name_prefix
  environment                = var.environment
  web_app_url                = var.web_app_url
  app_identity_principal_id  = var.app_identity_principal_id
  operator_object_id         = var.bootstrap_operator_object_id
  local_app_urls             = var.local_app_urls
  local_secret_lifetime_days = var.local_secret_lifetime_days
  create_demo_users          = var.create_demo_users
}
