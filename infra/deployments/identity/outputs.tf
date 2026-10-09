output "client_id" {
  value = module.identity.client_id
}

output "restricted_group_id" {
  value = module.identity.restricted_group_id
}

output "demo_user_sign_ins" {
  value     = module.identity.demo_user_sign_ins
  sensitive = true
}
