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
| Telemetry volume | Daily ingestion cap | Sized retention, archive to storage | The cap keeps cost bounded; dropped data is acceptable here | Audit or retention requirements |
