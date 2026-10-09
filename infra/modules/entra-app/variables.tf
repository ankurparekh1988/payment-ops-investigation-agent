variable "display_name" {
  description = "Name shown on the sign-in page and in My Apps."
  type        = string
}

variable "redirect_uris" {
  description = "HTTPS addresses Entra ID may send users back to after sign-in."
  type        = list(string)
}

variable "logout_url" {
  description = "Front-channel sign-out address."
  type        = string
  default     = null
}

variable "app_roles" {
  description = "App roles keyed by the value that appears in the roles claim."
  type = map(object({
    display_name = string
    description  = string
  }))
}

variable "role_id_namespace" {
  description = "Namespace for deriving stable app role IDs, unique per application."
  type        = string
}

variable "owner_object_ids" {
  description = "Object IDs of the application's owners."
  type        = list(string)
}
