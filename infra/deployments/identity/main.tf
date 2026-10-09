module "identity" {
  source = "../../stacks/identity"

  name_prefix        = var.name_prefix
  environment        = var.environment
  web_app_url        = var.web_app_url
  operator_object_id = var.bootstrap_operator_object_id
  local_app_urls     = var.local_app_urls
  create_demo_users  = var.create_demo_users
}
