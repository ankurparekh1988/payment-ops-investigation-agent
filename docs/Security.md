# Security

How access is controlled, as built so far. User sign-in, retrieval-time authorization, tool authorization and prompt-injection defences are added with the features they protect, and documented here when they land.

## Principles

- **No keys.** Every service-to-service call uses a managed identity with an Entra ID token. API keys, storage account keys and workspace keys are disabled, so there are no secrets to store, rotate or leak.
- **Least privilege, narrowest scope.** Each identity gets only the roles it needs, assigned on the specific resource rather than the subscription where possible.
- **Authority sits with people, not pipelines.** Granting roles to users, registering resource providers and creating pipeline identities require Owner rights, which no CI identity holds.
- **Only the pipeline changes environments.** The bootstrap, which creates the pipeline's identities and state, is the single step run by a person. Everything else is deployed only by the deployment pipeline, which is being added next; until then, nothing else is deployed.

## Identities

| Identity | Kind | Used by |
|---|---|---|
| `id-<prefix>-gh-plan` | User-assigned managed identity, GitHub OIDC | Pull request and `main` plan jobs |
| `id-<prefix>-gh-deploy` | User-assigned managed identity, GitHub OIDC | Approved apply and deploy jobs |
| `id-<prefix>-<env>-app` | User-assigned managed identity | The web app |
| Foundry project identity | System-assigned managed identity | Foundry's trace views |
| Developers | Entra ID users | Running the app locally against Azure (optional) |

## Role assignments

| Principal | Scope | Role | Why |
|---|---|---|---|
| Web app | Foundry resource | Cognitive Services OpenAI User | Call the chat and embedding deployments |
| Web app | Knowledge container | Storage Blob Data Reader | Serve source documents behind authorization checks |
| Web app | Application Insights | Monitoring Metrics Publisher | Send telemetry with an Entra token |
| Foundry project | Application Insights | Monitoring Metrics Publisher | Write traces, authenticating as the project |
| Foundry project | Application Insights | Log Analytics Reader | Show traces in the Foundry portal |
| `gh-plan` | Subscription | Reader | Plan infrastructure changes, read the model catalog, and detect drift in the bootstrap's subscription-level resources. Reader can't list keys or read secrets |
| `gh-plan` | State container | Storage Blob Data Reader | Read Terraform state |
| `gh-deploy` | Environment resource group | Contributor | Apply infrastructure changes |
| `gh-deploy` | Environment resource group | RBAC Administrator, conditional | Assign only allow-listed platform roles, only to service principals ([details](Deployment.md#pipeline-identities)) |
| `gh-deploy` | State container | Storage Blob Data Contributor | Read and write Terraform state |
| `gh-deploy` | Subscription | Model Availability Reader (custom) | Read the model catalog and quota for the model preflight; nothing else |
| Developers | Environment resource group | OpenAI User, Foundry User, Search Index Data Reader, Storage Blob Data Reader | Run the app locally |

## Keyless controls

| Resource | Setting |
|---|---|
| Foundry | Local (API key) authentication disabled |
| Storage accounts | Shared key access disabled; Entra ID is the default in the portal |
| Log Analytics | Local authentication disabled |
| Application Insights | Local authentication disabled; ingestion requires an Entra token |

Foundry's connection to Application Insights authenticates with the project's managed identity, so no connection string or key is stored in Foundry. The web app is given the Application Insights connection string only to know where to send telemetry; with local authentication disabled, sending still requires the app's Entra token.

## Pipeline logs

The repository is public, so workflow logs, artifacts and PR comments are too.

- Subscription, tenant and client IDs and the alert email are stored as GitHub secrets, so logs mask them wherever they appear, including inside resource IDs.
- Terraform redacts sensitive values in plans. Saved plan files contain them in plain text, so they never leave the job that created them.
- PR comments and job summaries list resource addresses and actions only, because GitHub doesn't mask them.
- Workflows from forks get no Azure access and need approval to run. Every third-party action is pinned to a commit SHA, and the default token is read-only.

## Content safety

Chat deployments use a strict content filter policy. Anything above "Safe" in the hate, sexual, violence and self-harm categories is blocked, on both prompts and completions, along with jailbreak attempts in user prompts and protected text in completions. Filtering is synchronous, so blocked output is never streamed. The trade-off is that some legitimate operational language may be blocked; that false-positive rate will be measured once the evaluation suite exists.

## Network

Endpoints are public and protected by Entra ID and TLS 1.2. Private networking is documented in [ProductionGapAnalysis](ProductionGapAnalysis.md).
