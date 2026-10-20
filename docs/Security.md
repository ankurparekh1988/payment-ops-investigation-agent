# Security

How access is controlled, as built so far. Tool authorization and prompt-injection defences are added with the features they protect, and documented here when they land.

## Principles

- **No key-based authentication.** Every service-to-service call uses a managed identity with an Entra ID token. API-key and shared-key authentication is disabled on every service, and no application or workflow code uses or passes a key. Some azurerm resources still read key material into Terraform state even with key access off; that material can't authenticate, and the known cases are listed below.
- **Least privilege, narrowest scope.** Each identity gets only the roles it needs, assigned on the specific resource rather than the subscription where possible.
- **Authority sits with people, not pipelines.** Granting roles to users, registering resource providers and creating pipeline identities require Owner rights, which no CI identity holds.
- **Only the pipeline changes environments.** Two steps are run by a person because they need rights no pipeline should hold: the bootstrap (the pipeline's own identities and state) and identity (the Entra app registration, groups and demo users). `scripts/terraform.sh` names these two and refuses changes to any other deployment outside the pipeline. Everything else is deployed only by the deployment pipeline, after approval.

## Users and sign-in

Users sign in with Entra ID ([ADR 0003](adr/0003-server-rendered-ui-and-sign-in.md)).

- **Assignment required.** Entra refuses anyone without a role assignment on the enterprise app, before they reach it.
- **Authorization code flow with PKCE.** The browser carries only a one-time code. The server redeems it, keeps the tokens on the server, and gives the browser an encrypted session cookie. The user is held in a scoped `IUserContext`, built from the token's claims, which every authorization decision reads.
- **No stored sign-in credential in Azure.** The deployed app redeems codes as its managed identity, which the app registration trusts through a federated credential: no client secret, certificate or credential in the app settings. Local development uses a separate, short-lived secret kept in .NET user-secrets, which exists only while local addresses are configured.
- **Demo users are opt-in.** They're ordinary tenant accounts, created only when `TF_VAR_create_demo_users=true` and assigned roles only on this application. Their passwords don't expire because they're shared for demonstrations, so remove them when not in use.
- **Everything requires a role** unless it's explicitly public: `/health`, `/health/ready`, sign-out and the signed-out and access-denied pages. That includes the source pages behind citations. A signed-in user without a role sees the access-denied page.
- **Sign-ins last a fixed time.** Roles and groups come from the token, so a sign-in ends after `PaymentOps:Authorization:SessionLifetime` (8 hours by default) without sliding, and changes made in Entra apply at the next one. Open pages are signed out at the same moment.

| App role | Can |
|---|---|
| `Ops.Reader` | Ask questions, run investigations, view citations |
| `Ops.Engineer` | Everything a Reader can, plus approve proposed actions |
| `Ops.Admin` | Everything an Engineer can, plus administration |

Higher roles include lower ones. Membership of the **Risk and Compliance** security group grants access to Restricted knowledge, and only when the group is configured: with no group id set, nobody gets Restricted knowledge. The token carries only groups assigned to the app, which keeps unrelated group IDs out of it and avoids overage for users in many groups.

## Identities

| Identity | Kind | Used by |
|---|---|---|
| `id-<prefix>-gh-plan` | User-assigned managed identity, GitHub OIDC | Pull request and `main` plan jobs |
| `id-<prefix>-gh-deploy` | User-assigned managed identity, GitHub OIDC | Approved apply and deploy jobs |
| `id-<prefix>-<env>-app` | User-assigned managed identity | The web app |
| Foundry project identity | System-assigned managed identity | Foundry's trace views |
| Developers | Entra ID users | Running the app locally against Azure (optional) |
| App users | Entra ID users and groups, assigned app roles | Signing in to the web app, including optional demo users per role |

## Role assignments

| Principal | Scope | Role | Why |
|---|---|---|---|
| Web app | Foundry resource | Cognitive Services OpenAI User | Call the chat and embedding deployments |
| Web app | Search service | Search Index Data Reader | Retrieve sections for questions and show cited sections |
| Web app | Application Insights | Monitoring Metrics Publisher | Send telemetry with an Entra token |
| Foundry project | Application Insights | Monitoring Metrics Publisher | Write traces, authenticating as the project |
| Foundry project | Application Insights | Log Analytics Reader | Show traces in the Foundry portal |
| `gh-plan` | Subscription | Reader | Plan infrastructure changes, read the model catalog, and detect drift in the bootstrap's subscription-level resources. Reader can't list keys or read secrets |
| `gh-plan` | Environment resource group | Web App Configuration Reader (custom) | Refresh the web app during plans, which reads its configuration through a list action Reader excludes. The app's settings hold no secrets |
| `gh-plan` | State container | Storage Blob Data Reader | Read Terraform state |
| `gh-deploy` | Environment resource group | Contributor | Apply infrastructure changes |
| `gh-deploy` | Environment resource group | RBAC Administrator, conditional | Assign only allow-listed platform roles, only to service principals ([details](Deployment.md#pipeline-identities)) |
| `gh-deploy` | State container | Storage Blob Data Contributor | Read and write Terraform state |
| `gh-deploy` | Environment resource group | Search Service Contributor, Search Index Data Contributor | Create the knowledge index and publish its sections |
| Bootstrap operator | Operator-only state container | Storage Blob Data Contributor | The identity deployment's state, which holds demo user passwords and the local sign-in secret. No pipeline identity has any role on this container |
| `gh-deploy` | Subscription | Model Availability Reader (custom) | Read the model catalog, quota and regional capacity for the preflight and the pre-apply capacity check; nothing else |
| Developers | Environment resource group | OpenAI User, Foundry User, Search Index Data Reader | Run the app locally |

## Keyless controls

| Resource | Setting |
|---|---|
| Foundry | Local (API key) authentication disabled |
| Storage accounts | Shared key access disabled; Entra ID is the default in the portal |
| Log Analytics | Local authentication disabled. The azurerm workspace resource still reads the workspace's shared keys into Terraform state, where they can't be used to authenticate |
| Application Insights | Local authentication disabled; ingestion requires an Entra token |
| Azure AI Search | API-key authentication disabled. Deployed through AzAPI rather than the azurerm resource, which would read the admin and query keys into Terraform state |

Foundry's connection to Application Insights authenticates with the project's managed identity. Foundry and the web app both hold the Application Insights connection string, but only to know where to send telemetry: with local authentication disabled, sending still requires an Entra token for the project or the app.

## Pipeline logs

The repository is public, so workflow logs, artifacts and PR comments are too.

- Subscription, tenant and client IDs and the alert email are stored as GitHub secrets, so logs mask them wherever they appear, including inside resource IDs.
- Terraform redacts sensitive values in plans. Saved plan files contain them in plain text, so a plan passed from the plan job to the apply job is encrypted with a key held as a secret, and kept for one day.
- PR comments and job summaries list resource addresses and actions only, because GitHub doesn't mask them.
- Workflows from forks get no Azure access and need approval to run. Every third-party action is pinned to a commit SHA, and the default token is read-only.

## AI release

Model choices, content filters and prompts are part of the build ([ADR 0004](adr/0004-ai-release-manifest.md)). They can't be changed at runtime: they change only through a pull request to `main`, which requires the CI checks to pass, including the release check that rejects a changed prompt without a higher version. Prompts are public, and the model can repeat them, so they must not contain secrets, URLs or access logic; today that rule is checked in review, and authorization is always enforced in code. Independent review of AI changes is a production gap ([ProductionGapAnalysis](ProductionGapAnalysis.md)).

## Knowledge classification

Knowledge documents are `general` (every signed-in user) or `restricted` (the Risk and Compliance group as well). The classification comes from the folder a source file is in, so it's visible in review and can't be set from inside a document. In the search index, each section carries its document's classification, and every query filters on it before anything reaches the model.

When a document becomes restricted, the publisher deletes its sections and confirms they're gone before uploading them again as restricted, so none of them is readable as general, even briefly. This follows the doc id: content moved into a restricted document with a different doc id leaves the old general sections in place until the end of the run, so a document is restricted by moving its file and keeping its doc id. The index holds restricted text, so it's readable only by identities with a Search data role: developers and the web app (Search Index Data Reader) and `gh-deploy`. Only `gh-deploy` can write to it. The publish log names doc ids and section paths, never section text.

The repository is public, so restricted sources are readable by anyone there. "Restricted" controls what the assistant shows to whom; it doesn't make the text secret, and nothing in the corpus may be real or sensitive. The build rejects numbers that pass the card checksum, and email addresses and `http`, `https` or `ftp` URLs outside reserved example domains. A bare host name in prose isn't checked, so review still matters.

Facts that decide outcomes, such as risk-rule thresholds, aren't in any document: they belong to the system that applies them.

## Structured data

The merchant, settlement, processor and error-event data is synthetic, and it isn't classified: everyone who can use the agent may see internal routing, processor responses and risk state, as the [product notes](PRD.md) set out. A risk hold therefore shows its rule id to every role; what the rule means, and how a hold is reviewed, is in a Restricted policy. Risk rule thresholds aren't in the data at all.

The dataset is in the public repository under the same checks as the knowledge sources: nothing that passes the card checksum, and only reserved example domains. Fault injection can't be switched on by configuration: only a test host enables it, in code, and a fault setting on its own stops the app from starting.

## Retrieval and answers

The model sees only sections the user may see, and the user sees only what the server has checked.

- **Retrieval-time authorization.** The classification filter is built from the signed-in user's identity inside the Knowledge module and added to every query. The question is search text only: it can't add to or change the filter. A user who isn't signed in gets no access at all.
- **Citations come from the server.** Each retrieved section gets an id for this question only. The model returns ids, never links, keys or titles; the server checks that the ids in the answer and the ids it declared are exactly the same set, and that each was retrieved for this question. One unknown or missing citation fails the whole answer.
- **Source pages re-check access.** `/sources/{key}` looks the section up with the user's current access in the same query, so a section they may not see returns the same 404 as one that doesn't exist. A key that isn't well formed is never sent to Search.
- **Rendering is an allowlist.** Model output and corpus text are rendered from a short list of Markdown elements, with all text encoded. Links, images, raw HTML and entities become plain text, so the model can't choose a URL, an image, markup or a link target.
- **Prompt guidance isn't a control.** The prompt tells the model to treat section text as evidence, never instructions, and the question and sections are delimited. That helps the model; it doesn't protect anything. The controls are the filter, the citation checks and the renderer.
- **No evidence, no model call.** With no sections the user may see, the server answers without calling the model, so the model can't fill the gap from its own knowledge.
- **Logs hold no content.** Answer logs record reason codes, counts, section keys, timings and token usage, and on failure the dependency, the exception type, status, error code and request id. They never record the question, section text, the prompt or the model's output, nor an exception's message, which can echo the request.
- **Calibration probes.** The retrieval comparison asks Restricted-only questions as a General user, in both modes, and fails if any Restricted section comes back ([Evaluation](Evaluation.md)).

## Content safety

Chat deployments use a strict content filter policy. Anything above "Safe" in the hate, sexual, violence and self-harm categories is blocked, on both prompts and completions, along with jailbreak attempts in user prompts and protected text in completions. Filtering is synchronous, so blocked output is never streamed. The trade-off is that some legitimate operational language may be blocked; that false-positive rate will be measured once the evaluation suite exists.

## Network

Endpoints are public and protected by Entra ID and TLS 1.2. Private networking is documented in [ProductionGapAnalysis](ProductionGapAnalysis.md).
