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

variable "bootstrap_operator_object_id" {
  description = "Entra object ID of the person who runs the human-run deployments."
  type        = string
}

variable "local_redirect_uris" {
  description = "Extra sign-in redirect addresses for running the app locally, as a JSON list."
  type        = list(string)
  default     = []
}

variable "create_demo_users" {
  description = "Create one demo user per role."
  type        = bool
  default     = false
}
