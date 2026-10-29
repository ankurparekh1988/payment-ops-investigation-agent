# Security

How access is controlled, as built so far. Each control is documented here when the feature it protects lands.

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
| `Ops.Engineer` | Everything a Reader can, plus approve or reject the tickets their own investigations propose |
| `Ops.Admin` | Everything an Engineer can, plus administration |

Higher roles include lower ones. Membership of the **Risk and Compliance** security group grants access to Restricted knowledge, and only when the group is configured: with no group id set, nobody gets Restricted knowledge. The token carries only groups assigned to the app, which keeps unrelated group IDs out of it and avoids overage for users in many groups.

## Identities

| Identity | Kind | Used by |
|---|---|---|
| `id-<prefix>-gh-plan` | User-assigned managed identity, GitHub OIDC | Pull request and `main` plan jobs |
| `id-<prefix>-gh-deploy` | User-assigned managed identity, GitHub OIDC | Approved apply and deploy jobs |
| `id-<prefix>-gh-eval` | User-assigned managed identity, GitHub OIDC | Evaluation runs: calls the model, reads the indexes |
| `id-<prefix>-gh-eval-publish` | User-assigned managed identity, GitHub OIDC | Publishing the evaluation index and recording evaluation history |
| `id-<prefix>-<env>-app` | User-assigned managed identity | The web app |
| Foundry project identity | System-assigned managed identity | Foundry's trace views |
| Developers | Entra ID users | Running the app locally against Azure (optional) |
| App users | Entra ID users and groups, assigned app roles | Signing in to the web app, including optional demo users per role |

## Role assignments

| Principal | Scope | Role | Why |
|---|---|---|---|
| Web app | Foundry resource | Cognitive Services OpenAI User | Call the chat and embedding deployments |
| Web app | Foundry resource | Prompt Shields User (custom) | Screen untrusted tool text; the role holds that one action |
| Web app | Search service | Search Index Data Reader | Retrieve sections for investigations and show cited sections |
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
| Developers | Foundry resource | Prompt Shields User (custom) | Screen tool text when running the app locally |
| `gh-eval` | Foundry account | Cognitive Services OpenAI User | Run the evaluated agent and the judge |
| `gh-eval` | Foundry account | Prompt Shields User (custom) | Screen tool text as the app does |
| `gh-eval` | Search service | Search Index Data Reader | Read the evaluation index, or the live index for a pull request that switches it |
| `gh-eval-publish` | The `knowledge-eval-v1` index only | Search Index Data Contributor | Make the evaluation index hold the evaluated change's corpus. Every run first proves the live index refuses its writes |
| `gh-eval-publish` | The evaluation history's data collection rule only | Monitoring Metrics Publisher | Write evaluation history rows; no other write in Azure Monitor |

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

When a document becomes restricted, the publisher deletes its sections and confirms they're gone before uploading them again as restricted, so none of them is readable as general, even briefly. This follows the doc id: content moved into a restricted document with a different doc id leaves the old general sections in place until the end of the run, so a document is restricted by moving its file and keeping its doc id. The index holds restricted text, so it's readable only by identities with a Search data role: developers, the web app and `gh-eval` (Search Index Data Reader), and `gh-deploy`. Only `gh-deploy` can write to it. The evaluation index, which only evaluations read, holds the same kind of text; only `gh-eval-publish` writes it. The publish log names doc ids and section paths, never section text.

The repository is public, so restricted sources are readable by anyone there. "Restricted" controls what the assistant shows to whom; it doesn't make the text secret, and nothing in the corpus may be real or sensitive. The build rejects numbers that pass the card checksum, and email addresses and `http`, `https` or `ftp` URLs outside reserved example domains. A bare host name in prose isn't checked, so review still matters.

Facts that decide outcomes, such as risk-rule thresholds, aren't in any document: they belong to the system that applies them.

## Structured data

The merchant, settlement, processor and error-event data is synthetic, and it isn't classified: everyone who can use the agent may see internal routing, processor responses and risk state, as the [product notes](PRD.md) set out. A risk hold therefore shows its rule id to every role; what the rule means, and how a hold is reviewed, is in a Restricted policy. Risk rule thresholds aren't in the data at all.

The dataset is in the public repository under the same checks as the knowledge sources: nothing that passes the card checksum, and only reserved example domains. Fault injection can't be switched on by configuration: only a test host enables it, in code, and a fault setting on its own stops the app from starting.

## Knowledge retrieval

The model sees only sections the user may see.

- **Retrieval-time authorization.** The classification filter is built from the signed-in user's identity inside the Knowledge module and added to every query. A search query is text only: it can't add to or change the filter. A user who isn't signed in gets no access at all.
- **Source pages re-check access.** `/sources/{key}` looks the section up with the user's current access in the same query, so a section they may not see returns the same 404 as one that doesn't exist. A key that isn't well formed is never sent to Search.
- **Rendering is an allowlist.** Corpus text is rendered from a short list of Markdown elements, with all text encoded. Links, images, raw HTML and entities become plain text.
- **Calibration probes.** The retrieval comparison asks Restricted-only questions as a General user, in both modes, and fails if any Restricted section comes back ([Evaluation](Evaluation.md)).

## Investigation tools and agent

The agent can only read, and only what the user may read; what it says is checked against what its tools actually returned.

- **Tools are read-only, and offered by role.** Every tool needs `Ops.Reader`. The agent is offered only the tools the user's role allows, and each tool checks the role again when it runs, so a tool is neither offered to nor executable by a user without it. Knowledge search uses the user's own access: a General user's investigation can't retrieve, register or cite a Restricted section, and isn't told one exists.
- **Arguments are validated before any data is read.** The server reads them strictly, whatever the schema sent to the model says: an argument the tool doesn't declare is an error and the tool doesn't run. Repeats are counted on the request as parsed, so an extra or blank argument doesn't make a new call. Ids are checked against a fixed pattern and matched exactly; ranges are capped at 31 days and can't end after the investigation's date. Errors go back to the model in fixed wording, never as exception text.
- **The tool loop fails closed.** Calls run one at a time; an unexpected exception or a call to an unknown tool ends the loop at once, with nothing from the exception sent to the model. Call, repeat, correction and time limits are enforced by our own code ([Architecture](Architecture.md#limits)).
- **Evidence comes from the server.** Only tool code registers evidence, from typed results; the model can cite an id but can't create one. An assessment citing anything this investigation's tools didn't return is rejected. Labels, outcome and confidence caps are set by the server.
- **Untrusted text is screened before the model reads it.** Every externally authored string a tool returns is marked and passed through Prompt Shields; what's flagged or can't be checked is withheld ([Injection and content safety](#injection-and-content-safety)). The structural controls hold whatever the screen decides: there are no write tools, evidence is registered by the server, every statement shown is validated against it, and a ticket needs a person's approval.
- **Earlier questions aren't evidence.** Follow-up questions see earlier turns as plain text with no tool messages and no section ids, and validation accepts only what this question's own tools returned. Only answered turns are carried: a blocked, refused or unassessed question is never sent to the model again.
- **Model text is never markup.** The investigation page binds every model-written string, including a proposed ticket's title and summary, as text. Timeline labels come from validated ids and dates in the parsed request, never from free text the model wrote.
- **Logs hold no content.** Investigation logs record the investigation id, outcome, confidence, label, counts of claims and gaps, timings and failure classes. They never record the question, tool results, the prompt or the model's output.

## Ticket approvals

Creating an incident ticket is the only action that changes anything outside the app, and the model can't take it ([ADR 0011](adr/0011-the-model-proposes-the-server-executes.md)).

| Asset | Where | Who can read it |
|---|---|---|
| Conversation and Reader drafts | The page's circuit, in memory | That user, until refresh, sign-out or restart |
| Pending proposals | Process memory, keyed by a server-generated id | Only through the approval service, which checks the owner on every call |
| Tickets | Process memory | The owner's card; there's no other read path |
| `approval.decided` and investigation logs | Application Insights | Identities with workspace read. Ids, the investigation id, the decision, a reason code, a pseudonymous approver id and the prompt and model version; never the title, summary, note, question, model text or a detected value |

| Threat | Control | Tested by |
|---|---|---|
| The model approves its own proposal | No ticket tool, no decision field, nothing the model returns reaches the approval service | A call to `create_incident_ticket` ends unverified with an empty store; text claiming approval leaves the proposal pending |
| A ticket before approval | Proposing never touches the ticket store | A valid proposal leaves the store empty |
| A Reader approves | No pending proposal exists for a Reader, and the role is read at call time | A Reader's assessment stores nothing; a Reader approving any id is refused |
| Another user approves | The owner check, answered like a missing id | Another Engineer gets "not found" |
| Double clicks or parallel calls | An atomic pending-to-executing step and the proposal id as idempotency key | 50 parallel approvals make one ticket |
| Replay after a decision | State | Approving after an approval or a rejection creates nothing |
| An old page approves new content | The next question, or a new investigation, supersedes the pending one | A superseded id is refused, with no ticket |
| Expiry | 15 minutes on the host's real clock | Real time +15 min 1 s is expired; the data's clock has no say |
| An ungrounded proposal | Processor and merchants must be records the investigation returned; data evidence and a runbook citation are both required | Each rule rejects, then one correction |
| A platform incident forced into one processor's ticket | Prompt rule, and missing files from two processors reject the ticket | Rejected |
| Card or account data in a ticket | `TicketContentPolicy`: card-number checksum, runs of eight or more digits, IBAN shapes; reports rule ids only | The correction, logs and result carry no digits from the input |
| Role removed mid-session | The role is read from the signed-in user at call time; a change in Entra applies at the next sign-in | Demoted to Reader before the click: refused |
| Cross-site request forgery | No HTTP endpoint for approvals; decisions go through the signed-in circuit | Endpoint enumeration finds none |
| Clickjacking | `frame-ancestors 'none'` on the interactive pages | A smoke test reads the header on `/` |
| A cancel races completion | A cancelled investigation keeps nothing | Cancelled after the submission: no proposal, no turn |
| Unbounded memory | 500 pending proposals, 1,000 tickets, Reader drafts outside the store | Full stores refuse with fixed results |

Known limits:

- A role removed in Entra applies at the next sign-in, as for every check.
- A person can approve a well-formed proposal that untrusted text steered. Flagged text is withheld and can't ground a ticket, but text the screen passes can still shape one; the approver sees the cited sources as they are ([Injection and content safety](#injection-and-content-safety)).
- The content policy blocks card-like and account-like numbers. It isn't data-loss prevention for bank details ([ProductionGapAnalysis](ProductionGapAnalysis.md)).
- Proposals and tickets are lost on restart, deploy or slot swap, and the app runs as one instance.
- A Reader's draft can't be handed to an Engineer; the Engineer runs the investigation themselves.

## AI evaluation gate

The `AI evaluation` check runs a pull request's own code with access to the model, so its controls are about what that code can reach.

| Threat | Control | Residual |
|---|---|---|
| A pull request runs arbitrary code with Azure access | A required reviewer approves each environment before any token is issued. `gh-eval` can only call the model and read the indexes in dev; `gh-eval-publish` can only write `knowledge-eval-v1` and the evaluation history | An approved pull request's code can spend tokens and read the dev indexes for the length of a run. Approval is the boundary |
| A fork or bot pull request | Untrusted: no environment, no token. The check fails if the change needs evaluating | A maintainer reproduces the change on a branch here |
| A pull request edits the classifier to skip evaluation | The classifier runs from the base commit, with the base's toolchain | — |
| A pull request edits `ai-evaluation.yml` | `pull_request` runs the pull request's own workflow file. The file is a gate-policy path, so under the base classifier's rule the same pull request can't change AI behaviour | A pull request that rewrites the workflow can also remove that rule; rulesets on a personal account can't pin a required workflow to `main`'s version. The control is the maintainer's review of every `.github/workflows` change |
| The gate is weakened alongside a behaviour change | Gate policy and behaviour change in separate pull requests, and a threshold or dataset change needs an ADR ([Evaluation](Evaluation.md#running-in-ci)) | A policy-only weakening is visible in its own pull request and ADR |
| An approved pull request's code writes to Search | Only `gh-eval-publish` can write, only in the publish job, and only to `knowledge-eval-v1`. Every run first deletes a key that doesn't exist from the live index and requires a 403, so a widened role fails the run instead of going unnoticed | It can put anything in the evaluation index, which only evaluations read. Each run reconciles it first |
| The model-calling identity changes Search or the history | `gh-eval` has read roles only, and the identity that writes is federated to different environments | — |
| Two evaluations interleave in the shared index | One workflow-wide concurrency group for the environment, held from publishing to evaluating, never cancelled mid-run, with later runs queued | — |
| Evaluation reads Restricted knowledge | `gh-eval` is a service principal in no group; the harness's simulated users decide access, exactly as the app does | Same as the app: access is enforced in the knowledge tool, not by Search roles |
| A scheduled run is used to skip approval | The `-scheduled` environments have no reviewer but deploy from protected branches only, so only `main` can use them | — |
| The required check writes to the repository | `AI evaluation` has no permissions. Only `report-scheduled`, which never runs on a pull request, can write issues | — |

Evaluation history rows carry metric names, numbers, verdicts, the commit, prompt and model versions, and the trigger. They never carry model text, case content, endpoints or identifiers. Only runs of `main` are recorded.

## Injection and content safety

Two controls with different jobs: the deployment's content filter screens what the person asks and what the model answers, and Prompt Shields screens the text tools return, which the filter never sees ([ADR 0013](adr/0013-screen-untrusted-tool-text.md)).

| Threat | Where it enters | Control |
|---|---|---|
| Harmful content in a question or an answer | The question; the completion | Strict content filter on prompts and completions; the investigation ends *blocked* |
| A direct prompt attack | The question | The filter's prompt-attack detection; the investigation ends *blocked*, and the question is never resent |
| An instruction hidden in data or documents | Names, incident notes, references, closure reasons, error codes, knowledge | Marked where the tool writes it, screened by Prompt Shields, withheld when flagged |
| A screen that can't answer | Throttling, errors, timeouts | The text is withheld as unchecked; the investigation continues on structured data |
| Steered support for a claim or a ticket | A withheld knowledge section | It keeps its citation id but can't support anything |
| An attack carried into later questions | Conversation history | Only answered turns are carried |

**The content filter.** Chat deployments use a strict policy. Anything above "Safe" in the hate, sexual, violence and self-harm categories is blocked, on both prompts and completions, along with prompt attacks in user prompts and protected text in completions. Filtering is synchronous, so blocked output is never streamed. A rejected prompt or a withheld completion ends the investigation as *blocked*, and the person sees "This question can't be investigated: it was blocked by the content safety policy." Nothing partial is shown, and the request isn't retried. Some legitimate operational language may be blocked; the safety evaluation measures how often.

**What's screened.** Trust follows provenance, not appearance. These are written outside the system, and each is marked and screened wherever it appears:

| Text | Tool |
|---|---|
| Knowledge title, section path and content | `search_knowledge` |
| Merchant name | `get_merchant_profile` |
| Processor name | `get_merchant_profile`, `get_processor_status`, `get_settlement_batches` |
| Incident summary and the processor's reference | `get_processor_status` |
| Closure-day reason | `get_settlement_batches` |
| Error code | `get_error_events` |

Ids, dates, amounts, enum values and time zones checked against the IANA list stay plain. A test poisons every string in the data and the knowledge, runs every tool, and fails if any poison appears unmarked, so a new field can't slip past. Evidence labels, which name merchants and processors, are built by the server and shown only to people; the model never reads them.

**Fail closed.** A flagged value is replaced by "[withheld: this text was flagged as a possible instruction to the assistant]"; one that couldn't be checked, by "[withheld: this text couldn't be checked]". The result shows a gap saying which source lost text and why. Without the Content Safety endpoint, which only Development allows, nothing can be checked and every untrusted value is withheld. The log records how many values were screened, flagged and unscreenable, never the text.

**Withheld knowledge supports nothing.** A section with any part withheld can't support a statement, an *Assessment* root cause, high confidence or a ticket; an assessment citing it goes back for correction.

**The human source boundary.** The source page shows an authorized person the original section, re-read from the index. Prompt Shields protects the model's context, not what a person reads; a person deciding on a ticket sees the cited sources as they are.

**False positives.** Before screening went live, the corpus the index holds and the demo data, exactly as the model sees them, were screened: 304 strings (216 distinct), none flagged. A poisoned incident note was flagged.

**Processing location.** Prompt Shields appears for Canada Central under Foundry guardrails but not under the standalone Content Safety service. Our calls go to the Canada Central Foundry account and succeed, but that doesn't prove where the operation is processed. Until Microsoft documents it, treat screened text as possibly processed outside the region; the data here is synthetic.

## Network

Endpoints are public and protected by Entra ID and TLS 1.2. Private networking is documented in [ProductionGapAnalysis](ProductionGapAnalysis.md).
