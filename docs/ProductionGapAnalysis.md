# Production gap analysis

What a production deployment would need beyond this repository, why each item isn't built here, and what would trigger building it. The list grows as the system does.

| Area | Here | In production | Why it's deferred | Trigger to build it |
|---|---|---|---|---|
| Network isolation | Public endpoints, Entra ID and TLS | Private endpoints for Foundry, Storage and Search; App Service VNet integration; public access disabled | Adds cost and networking setup without changing how the AI system behaves | Any real data, or a security policy requiring it |
| App hosting tier | F1: no SLA, no always-on, CPU quota | B1 or higher, multiple instances, deployment slots | A demo workload doesn't need it | Real users or an availability target |
| Search tier | Serverless (preview) | Basic or Standard, GA, with SLA | Preview is acceptable where no SLA is needed | Production traffic, or serverless reaching GA |
| Environments | `dev` only; one Foundry resource | Separate subscriptions or resource groups per environment, each with its own Foundry resource and state | Doubles cost for no new evidence; the code is already environment-aware | A second team or a production rollout |
| Region resilience | Single region | Secondary region with a tested failover | Global Standard model deployments already route across regions | A recovery-time objective that one region can't meet |
| Spending control | Budget alerts by email | Alerts routed to on-call, plus automated limits on token usage | Alerts are enough when one person runs the system | Shared or unattended use |
| Human access | The owner holds standing Owner rights; scripts refuse local applies, but that's a guardrail rather than a control | No standing write access: just-in-time elevation (Privileged Identity Management) for break-glass only, enforced by Azure Policy | One person operates the system | More than one operator, or a compliance requirement |
| Encryption keys | Microsoft-managed keys | Customer-managed keys in Key Vault for storage and Foundry | Synthetic data doesn't justify key management | Real customer data or a regulatory requirement |
| Storage audit logs | Entra-only, role-scoped access; no data-plane logs | Blob read/write diagnostic logs to Log Analytics for the state and knowledge containers | Adds ingestion cost for little signal at this scale | Real data, or any need to audit who read the corpus or state |
| Telemetry volume | Daily ingestion cap | Sized retention, archive to storage | The cap keeps cost bounded; dropped data is acceptable here | Audit or retention requirements |
