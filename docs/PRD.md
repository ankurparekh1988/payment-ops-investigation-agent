# Product Requirements: Payment Ops Investigation Agent

*Contoso Payments is a fictional company. All data in this repository is synthetic.*

## 1. Problem

Contoso Payments' operations team investigates payout delays, settlement failures and processor issues. Today an engineer reconstructs each incident by hand: querying settlement records, scanning error logs, checking processor status pages and finding the right runbook. This is slow, depends on individual knowledge, and leaves no consistent record of how a conclusion was reached.

The agent performs the cross-referencing, cites every piece of evidence, states its confidence, and proposes next actions. People keep the decisions.

## 2. Personas and roles

| Persona | App role | Can do |
|---|---|---|
| Ops Analyst | `Ops.Reader` | Ask questions, run investigations, view citations |
| Ops Engineer | `Ops.Engineer` | Everything above, plus approve state-changing actions |
| Platform Admin | `Ops.Admin` | Everything above, plus re-index knowledge and switch the demo dataset |
| Risk & Compliance *(security group)* | — | Additionally retrieve **Restricted** knowledge documents |

## 3. Investigation scenarios

| ID | Scenario | What it demonstrates |
|---|---|---|
| S1 | Payouts delayed for a merchant because of a processor outage window | Correlation across several tools, runbook retrieval, approval-gated incident ticket |
| S2 | Settlement not received because a bank holiday caused a missed cut-off | Reasoning over dates and policy documents |
| S3 | A merchant's payouts are paused by a risk hold whose explanation is in a Restricted policy | Document-level security trimming: different answers for different users |
| S4 | A duplicate settlement batch | Data anomaly detection with cited evidence |
| S5 | A merchant support note contains an instruction aimed at the assistant | Defence in depth against indirect prompt injection |

Every tool must be required by at least one scenario.

## 4. Functional requirements

1. **Investigate with tools.** The agent chooses which read-only tools to call (merchant profile, settlement batches, error events, processor status, knowledge search) and with which arguments. No sequence is hard-coded.
2. **Cite evidence.** Every factual claim cites a retrieved knowledge passage or a data evidence id. The server rejects citations that were not retrieved in the current turn.
3. **Refuse rather than guess.** When evidence is missing or inaccessible, the agent says so and suggests escalation.
4. **Respect authorization.** Knowledge retrieval is filtered by the user's group membership on the server. The model never sees documents the user may not read, and never learns that they exist.
5. **Propose, don't act.** The only state-changing tool, creating an incident ticket, is offered only to Ops Engineers and above, requires explicit approval, and re-checks authorization when it executes.
6. **Show the work.** The UI streams a tool timeline and renders the final assessment only after server-side validation.
7. **No financial or risk actions.** Releasing or holding payouts, changing merchant status and retrying settlements have no tool at all.

## 5. Non-functional requirements

| Area | Requirement |
|---|---|
| Latency | p95 time to first token ≤ 3 s; p95 full investigation ≤ 20 s |
| Reliability | Tool error rate < 2% with fault injection off; at most 10 tool calls and 60 s per turn |
| Security | 0 authorization leaks on the authorization evaluation set; 0 injected actions executed; key-based authentication disabled on every Azure AI and data service |
| Quality | Groundedness ≥ 4.0/5; retrieval recall@5 ≥ 0.85; citation precision ≥ 0.95; tool selection accuracy ≥ 0.90 |
| Cost | Per-user rate limiting; per-turn cost estimate; budget alerts |
| Privacy | Raw prompts and completions are not logged by default |

## 6. Success metrics

- **Assessment acceptance rate:** assessments that receive positive feedback or lead to an approved ticket.
- **Proposal acceptance and edit rate:** tickets approved unedited, edited, or rejected.
- **Escalation rate:** investigations that end as escalated. A very low rate can signal overconfidence.
- **Cost per resolved investigation.**

With no real users, these are instrumented and measured on the demo scenarios, and reported as indicative.

## 7. Scope and non-goals

**Out of scope:** merchant onboarding, KYC/KYB, sanctions screening, case management and any compliance decisioning; a real payments backend or database; multi-agent orchestration; document upload; real personal or card data; vision and speech.

## 8. Assumptions

- One deployed environment (dev), one region, public endpoints over TLS.
- English only, with two knowledge sensitivity tiers.
- Conversation threads, pending approvals and demo tickets are held in memory and do not survive restarts.
