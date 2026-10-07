terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.8"
    }
  }
}

resource "azurerm_monitor_action_group" "this" {
  name                = var.action_group_name
  resource_group_name = var.resource_group_name
  short_name          = substr(var.action_group_short_name, 0, 12)

  email_receiver {
    name                    = "owner"
    email_address           = var.alert_email
    use_common_alert_schema = true
  }

  tags = var.tags
}

resource "azurerm_consumption_budget_resource_group" "this" {
  name              = var.budget_name
  resource_group_id = var.resource_group_id
  amount            = var.monthly_amount
  time_grain        = "Monthly"

  time_period {
    # Must be the first of a month; only meaningful at creation, so later plans ignore it.
    start_date = formatdate("YYYY-MM-01'T'00:00:00Z", plantimestamp())
  }

  dynamic "notification" {
    for_each = var.actual_cost_thresholds
    content {
      enabled        = true
      threshold      = notification.value
      threshold_type = "Actual"
      operator       = "GreaterThanOrEqualTo"
      contact_groups = [azurerm_monitor_action_group.this.id]
    }
  }

  notification {
    enabled        = true
    threshold      = 100
    threshold_type = "Forecasted"
    operator       = "GreaterThanOrEqualTo"
    contact_groups = [azurerm_monitor_action_group.this.id]
  }

  lifecycle {
    ignore_changes = [time_period]
  }
}
