module "identity" {
  source = "../../stacks/identity"

  name_prefix         = var.name_prefix
  environment         = var.environment
  operator_object_id  = var.bootstrap_operator_object_id
  local_redirect_uris = var.local_redirect_uris
  create_demo_users   = var.create_demo_users
}
