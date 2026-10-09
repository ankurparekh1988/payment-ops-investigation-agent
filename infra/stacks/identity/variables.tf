variable "name_prefix" {
  description = "Short lowercase prefix used in resource names."
  type        = string
}

variable "environment" {
  description = "Environment whose web app users sign in to, for example dev."
  type        = string
}

variable "operator_object_id" {
  description = "Entra object ID of the person who runs this deployment. Owns the objects and gets Ops.Admin."
  type        = string
}

variable "local_redirect_uris" {
  description = "Extra sign-in redirect addresses for running the app locally."
  type        = list(string)
  default     = []
}

variable "create_demo_users" {
  description = "Create demo users for each role, to show how answers and actions differ by role."
  type        = bool
  default     = false
}
