# Deployment

How the infrastructure is organised, and how to set it up in your own Azure subscription.

## How it's organised

All Azure infrastructure is Terraform, in three layers ([ADR 0001](adr/0001-terraform-for-infrastructure.md)):

```text
infra/
├─ modules/        building blocks: state-storage, github-oidc-identity
├─ stacks/         compositions: bootstrap
└─ deployments/    where a stack meets a real subscription: backend, providers, values from env
```

There are two kinds of deployment:

| | What it creates | Who runs it |
|---|---|---|
| **Bootstrap** | Terraform state storage, the GitHub pipeline identities, the environment's resource group | A person with Owner rights, once. Re-run only when the bootstrap itself changes |
| **Platform** *(coming next)* | Everything the application runs on | The GitHub Actions pipeline |

The bootstrap can't run through the pipeline, because it creates what the pipeline needs to run: the identity the pipeline signs in as, and the storage that holds its state. Creating them also needs Owner-level rights, which shouldn't sit with any CI identity. So a person runs it once with reviewed Terraform code, and from then on the pipeline works inside the boundaries it set up.

## Prerequisites

- An Azure subscription where you're **Owner** (needed for the bootstrap only)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), signed in with `az login`
- [Terraform](https://developer.hashicorp.com/terraform/install) 1.10 or later
- Bash. On Windows, Git Bash works.

## Configuration

Nothing environment-specific is committed. Copy `.env.example` to `.env` (ignored by git) and fill it in:

| Variable | Example | Purpose |
|---|---|---|
| `ARM_SUBSCRIPTION_ID` | | Subscription to deploy into |
| `ARM_TENANT_ID` | | Entra tenant |
| `TF_VAR_location` | `canadacentral` | Region for every resource |
| `TF_VAR_name_prefix` | `paymentops` | Prefix for resource names |
| `TF_VAR_environment` | `dev` | Environment to prepare |
| `TF_VAR_github_repository` | `owner/name` | The only repository whose workflows can sign in to Azure |
| `TF_STATE_*` | | Written by the bootstrap script on its first run |

## Bootstrap

```bash
az account set --subscription <subscription-id>
scripts/bootstrap.sh
```

On the first run the script:

1. applies the bootstrap stack with local state;
2. records where the state storage is in `.env`;
3. moves its own state into that storage, then deletes the local copy.

Later runs apply against the remote state directly. To check for drift, run the script and review the plan before confirming.

### What it creates

| Resource | Notes |
|---|---|
| `rg-<prefix>-bootstrap` | Long-lived: state storage and pipeline identities |
| `rg-<prefix>-<environment>` | The environment's resource group, initially empty |
| State storage account | Zone-redundant. Entra ID only (account keys disabled), TLS 1.2, versioning, 30-day soft delete for blobs and containers, and a delete lock |
| `id-<prefix>-gh-plan` | Pipeline identity for read-only plans |
| `id-<prefix>-gh-deploy` | Pipeline identity for approved deployments |

The cost is a few cents a month.

## Pipeline identities

GitHub Actions signs in to Azure with OpenID Connect: each workflow run gets a short-lived token from GitHub, which Azure exchanges for access. No credentials are stored in GitHub. Each identity trusts only specific workflow contexts in this repository, and a fork's workflows are rejected because their tokens name a different repository.

```mermaid
flowchart LR
  PR["Pull request"] --> P["gh-plan"]
  MAIN["Push to main<br/>(plan job)"] --> P
  ENVI["GitHub environment<br/>dev-infra (approval)"] --> D["gh-deploy"]
  ENVA["GitHub environment<br/>dev (approval)"] --> D
  P -->|Reader| RG["rg-…-dev"]
  P -->|read state| ST[("tfstate")]
  D -->|Contributor + limited role granting| RG
  D -->|read/write state| ST
```

| Identity | Trusted contexts | Permissions |
|---|---|---|
| `gh-plan` | Pull requests; the `main` branch | Reader on the environment's resource group; read-only access to state (plans run with `-lock=false`) |
| `gh-deploy` | GitHub environments `<env>-infra` and `<env>` only | Contributor on the environment's resource group; read/write state; limited role granting (below) |

A pull request can show what *would* change, but cannot change anything.

**Limited role granting.** The platform uses managed identities with key-based access disabled everywhere, so deploying it involves granting roles. `gh-deploy` holds *Role Based Access Control Administrator* with a condition attached. It can grant only the data-plane roles the platform needs, and only to service principals such as managed identities:

- Cognitive Services OpenAI User
- Cognitive Services User
- Foundry User
- Search Index Data Reader
- Search Index Data Contributor
- Search Service Contributor
- Storage Blob Data Reader
- Storage Blob Data Contributor
- Log Analytics Reader

It can't grant Owner or Contributor, and can't grant anything to a user account.

## Environments

`dev` is the deployed environment. The bootstrap is environment-aware: running it with `TF_VAR_environment=prod` prepares a separate resource group and deployment identity using the same code.

## Removing the bootstrap

The state storage has a delete lock on purpose. To remove everything, first destroy any deployed environments, then delete the lock and run `terraform destroy` in `infra/deployments/bootstrap`.
