# Product notes

*Contoso Payments is a fictional company. All data in this repository is synthetic.*

## Setting

Contoso Payments is a payment orchestration platform: it routes merchants' payments to downstream processors and acquirers and pays merchants out from their settlement files.

The agent is an internal tool for Contoso's operations, engineering, admin and risk teams, and merchants never use it. That's why it may see routing, processor responses and internal risk state.

## Problem

Contoso Payments' operations team investigates payout delays, settlement failures and processor issues. Today an engineer reconstructs each incident by hand: querying settlement records, scanning error logs, checking processor status pages and finding the right runbook. This is slow, depends on individual knowledge, and leaves no consistent record of how a conclusion was reached.

The agent performs the cross-referencing, cites every piece of evidence, states its confidence, and proposes next actions. People keep the decisions.

## Who uses it

| Persona | App role | Can do |
|---|---|---|
| Ops Analyst | `Ops.Reader` | Ask questions, run investigations, view citations |
| Ops Engineer | `Ops.Engineer` | Everything above, plus approve state-changing actions |
| Platform Admin | `Ops.Admin` | Everything above, plus pause and resume investigations for everyone |
| Risk & Compliance *(security group)* | — | Additionally retrieve **Restricted** knowledge documents |

## Scenarios

| ID | Scenario | What it exercises |
|---|---|---|
| S1 | Payouts delayed for a merchant because of a processor outage window | Correlation across several tools, runbook retrieval, approval-gated incident ticket |
| S2 | Settlement not received because a settlement-system closure day followed a missed cut-off | Reasoning over dates and policy documents |
| S3 | A merchant's payouts are paused by a risk hold whose explanation is in a Restricted policy | Document-level security trimming: different answers for different users |
| S4 | A duplicate settlement batch | Data anomaly detection with cited evidence |
| S5 | A merchant support note contains an instruction aimed at the assistant | Defence in depth against indirect prompt injection |

Every tool must be required by at least one scenario.

## What it does

1. **Investigate with tools.** The agent chooses which read-only tools to call (merchant profile, settlement batches, error events, processor status, knowledge search) and with which arguments. No sequence is hard-coded.
   Facts come from tools: payment and settlement state, processor configuration, outages and risk holds. Knowledge documents hold only what explains those facts or says what to do about them: runbooks, processor operating guides, policies, escalation procedures and postmortems.
2. **Cite evidence.** Every factual claim cites a retrieved knowledge passage or a data evidence id. The server rejects citations that were not retrieved in the current turn.
3. **Refuse rather than guess.** When evidence is missing or inaccessible, the agent says so and suggests escalation.
4. **Respect authorization.** Knowledge retrieval is filtered by the user's group membership on the server. The model never sees documents the user may not read, and never learns that they exist.
5. **Propose, don't act.** The only state-changing action, creating an incident ticket, is proposed by the agent and executed by the server only after the investigating Ops Engineer approves it; authorization is re-checked when it executes.
6. **Show the work.** The UI shows a live timeline of the agent's tool calls, written by the server, and renders the final assessment only after server-side validation.
7. **No financial or risk actions.** Releasing or holding payouts, changing merchant status and retrying settlements have no tool at all.

## Targets and outcomes

The targets below were set when the product was designed, before anything was measured. They aren't the release gate: the gate's thresholds come from the measured run-to-run spread of a baseline ([Evaluation](Evaluation.md#the-threshold-rule)).

Measured outcomes come from the release evaluation of 2026-11-18 ([summary](evidence/release-evaluation-2026-11-18.md)), 37 labelled cases × 3 runs, 26 held-out retrieval questions and 87 safety cases in one run, unless a row says otherwise. They're results on those evaluated cases, not guarantees. Where the evaluation measures something close to a target but not the same thing, the row says so.

| Area | Design target | Outcome | Kind |
|---|---|---|---|
| Latency | p95 time to first progress ≤ 3 s (from submitting a question to the first tool call or submission the agent makes) | 2.4 s | Measured, reported, not gating |
| Latency | p95 full investigation ≤ 20 s | 13.1 s | Measured, reported, not gating |
| Reliability | Tool error rate < 2% with fault injection off | Not measured as a rate. A failed or cut-short source is recorded as a gap in the assessment | — |
| Reliability | At most 10 tool calls and 60 s per turn | Enforced, with limits on model calls and tokens ([Architecture](Architecture.md#limits)) | Implemented control |
| Security | 0 authorization leaks on the authorization evaluation set | 0 across every run of the investigation and safety cases | Measured, gating |
| Security | 0 injected actions executed | 0 unauthorized state changes and 0 planted instructions followed | Measured, gating |
| Security | Key-based authentication disabled on every Azure AI and data service | Disabled; every call uses an Entra identity ([Security](Security.md)) | Implemented control |
| Quality | Groundedness ≥ 4.0/5 | 4 from the judge on 2026-10-25 ([record](Evaluation.md#proving-the-gate-2026-10-25)); the judge doesn't gate and wasn't run on 2026-11-18 | Measured once, not gating |
| Quality | Retrieval recall@5 ≥ 0.85 | 0.769 on held-out questions, below the design target; the gate's threshold is 0.76 | Measured, gating at 0.76 |
| Quality | Citation precision ≥ 0.95 | Not measured as defined. The server rejects any citation this investigation didn't retrieve; expected-section precision, a stricter comparison with labelled sections, is 0.773 | Implemented control; related metric gating |
| Quality | Tool selection accuracy ≥ 0.90 | Not measured as defined. Required-tool recall is 0.791 and tool-argument accuracy 0.630 | Related metrics, gating |
| Cost | Per-user rate limiting; per-turn cost estimate; budget alerts | Daily limits per person and in total, an estimated cost per investigation, budget and anomaly alerts ([ADR 0021](adr/0021-bounded-usage-and-observable-cost.md)) | Implemented control |
| Privacy | Raw prompts and completions are not logged by default | Logs and decision records hold ids, counts and timings, never the question, tool output or model text | Implemented control |

## How success is measured

- **Assessment acceptance rate:** assessments that receive positive feedback or lead to an approved ticket.
- **Proposal approval and rejection rate:** proposed tickets approved, or rejected with a reason. An edit rate needs editing, which doesn't exist yet.
- **Escalation rate:** investigations that end as escalated. A very low rate can signal overconfidence.
- **Cost per resolved investigation.**

These are the measures for real operational use. There are no real users, so none of them has been measured. What is measured is the evaluation on labelled cases: outcomes, tool use, evidence, safety and the modelled cost per validated investigation ([Evaluation](Evaluation.md)).

## Out of scope

Merchant onboarding, KYC/KYB, sanctions screening, case management and any compliance decisioning; a real payments backend or database; multi-agent orchestration; document upload; real personal or card data; vision and speech.

## Assumptions

- One deployed environment (dev), one region, public endpoints over TLS.
- English only, with two knowledge sensitivity tiers.
- Conversation threads, pending approvals and demo tickets are held in memory and do not survive restarts.
