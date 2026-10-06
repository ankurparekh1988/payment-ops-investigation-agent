# 0001. Terraform for infrastructure

- **Status:** Accepted
- **Date:** 2026-10-06

## Context

Everything this system runs on lives in Azure: model deployments, search, storage, hosting, identities, role assignments and monitoring. That infrastructure needs to be reproducible from the repository, reviewable before it changes, and deployable by a pipeline that holds no stored credentials.

Infrastructure changes should be reviewed the same way code is: in a pull request, by reading exactly what will change.

## Decision

Use **Terraform** with the AzureRM provider, and AzAPI only where AzureRM doesn't yet cover a newer Azure capability. State is kept in Azure Storage with Entra ID authentication.

The code is organised in three layers:

| Layer | Folder | Responsibility |
|---|---|---|
| Modules | `infra/modules/` | One building block each, such as state storage or a GitHub OIDC identity. No provider configuration, no backend, nothing specific to this project's environments. |
| Stacks | `infra/stacks/` | Compose modules into something deployable: `bootstrap` now, the application platform next. Reusable for any subscription or region. |
| Deployments | `infra/deployments/` | The only layer with a backend and provider configuration. Binds a stack to a real subscription, using values from environment variables. |

Nothing environment-specific is committed. Subscription, region, naming prefix, environment and repository come from environment variables (`.env` locally, repository variables in CI). Anyone can deploy the same code into their own subscription without editing it.

## Consequences

- Every infrastructure change shows up as a `terraform plan` in its pull request before anything is applied.
- Drift is visible: a plan against the live environment reports anything changed outside Terraform.
- State must be secured and operated. It sits in a dedicated storage account with no account keys, versioning, 30-day soft delete and a delete lock.
- The state storage and pipeline identities have to exist before any pipeline can run, so a one-time bootstrap is run by a person with Owner rights (see [Deployment](../Deployment.md)). Everything after that is deployed by the pipeline with narrower permissions.
- New Foundry features sometimes reach AzureRM later than the Azure APIs. AzAPI fills those gaps, with a comment on each AzAPI resource naming the gap so it can move to AzureRM later.

## Alternatives considered

- **Bicep with the Azure Developer CLI.** First-party, and no state to manage. Rejected because its preview of changes (what-if) is less reliable than a Terraform plan for review, and drift detection is weaker. Terraform also carries over to work outside Azure.
- **Portal or CLI scripts.** Not reproducible or reviewable, so not considered further.
