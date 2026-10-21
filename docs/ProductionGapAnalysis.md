# Production gap analysis

What a production deployment would need beyond this repository, why each item isn't built here, and what would trigger building it. The list grows as the system does.

| Area | Here | In production | Why it's deferred | Trigger to build it |
|---|---|---|---|---|
| Network isolation | Public endpoints, Entra ID and TLS | Private endpoints for Foundry, Storage and Search; App Service VNet integration; public access disabled | Adds cost and networking setup without changing how the AI system behaves | Any real data, or a security policy requiring it |
| App hosting tier | F1: no SLA, no always-on, CPU quota | B1 or higher, multiple instances, deployment slots | A demo workload doesn't need it | Real users or an availability target |
| Search tier | Free: no SLA, 50 MB, and an idle service may be deleted (the next deployment recreates it) | Basic or Standard, with an SLA and replicas | Free holds the knowledge corpus with room to spare, at no cost | Production traffic, or an availability target |
| Environments | `dev` only; one Foundry resource | Separate subscriptions or resource groups per environment, each with its own Foundry resource and state | Doubles cost for no new evidence; the code is already environment-aware | A second team or a production rollout |
| Region resilience | Single region | Secondary region with a tested failover | Global Standard model deployments already route across regions | A recovery-time objective that one region can't meet |
| Systems of record | One synthetic dataset built into the app, read in memory | Adapters over the real merchant, payments, payout, processor and log systems, with paging, freshness and their own failure handling | The agent's behaviour is what's being built; the data ports and their contract tests are what a real adapter has to satisfy | Any real data source |
| Per-user limits | One question at a time per open page, with a token cap per answer | Per-user rate limits and token budgets, enforced across instances | A single instance and a handful of users don't need shared state | Shared use, more than one instance, or unexpected model spend |
| Spending control | Budget alerts by email | Alerts routed to on-call, plus automated limits on token usage | Alerts are enough when one person runs the system | Shared or unattended use |
| Human access | The owner holds standing Owner rights; scripts refuse local applies, but that's a guardrail rather than a control | No standing write access: just-in-time elevation (Privileged Identity Management) for break-glass only, enforced by Azure Policy | One person operates the system | More than one operator, or a compliance requirement |
| Encryption keys | Microsoft-managed keys | Customer-managed keys in Key Vault for storage and Foundry | Synthetic data doesn't justify key management | Real customer data or a regulatory requirement |
| Storage audit logs | Entra-only, role-scoped access; no data-plane logs | Blob read/write diagnostic logs to Log Analytics for the state and knowledge containers | Adds ingestion cost for little signal at this scale | Real data, or any need to audit who read the corpus or state |
| Review of AI changes | Pull requests to `main` require passing CI checks; there's one maintainer, so no independent approval | Code-owner approval required for `ai/**`, from someone other than the author | An author can't independently approve their own change | A second maintainer |
| Telemetry volume | Daily ingestion cap | Sized retention, archive to storage | The cap keeps cost bounded; dropped data is acceptable here | Audit or retention requirements |
