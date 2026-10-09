# Values come from TF_VAR_* environment variables (see .env.example).

variable "name_prefix" {
  description = "Short lowercase prefix used in resource names."
  type        = string
}

variable "environment" {
  description = "Environment whose web app users sign in to, for example dev."
  type        = string
}

variable "web_app_url" {
  description = "HTTPS address of the deployed web app (scripts/identity.sh reads it from the platform)."
  type        = string
}

variable "app_identity_principal_id" {
  description = "Principal ID of the web app's managed identity (scripts/identity.sh reads it from the platform)."
  type        = string
}

variable "bootstrap_operator_object_id" {
  description = "Entra object ID of the person who runs the human-run deployments."
  type        = string
}

variable "local_app_urls" {
  description = "Base addresses the app runs on locally, as a JSON list."
  type        = list(string)
  default     = []
}

variable "local_secret_lifetime_days" {
  description = "Days before the local-development client secret expires; rerunning the deployment after that replaces it."
  type        = number
  default     = 30
}

variable "create_demo_users" {
  description = "Create one demo user per role."
  type        = bool
  default     = false
}
