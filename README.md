# Payment Ops Investigation Agent

[![CI](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/workflows/ci.yml/badge.svg)](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/workflows/ci.yml)
[![CD](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/workflows/cd.yml/badge.svg)](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/workflows/cd.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Blazor](https://img.shields.io/badge/Blazor-Interactive%20Server-512BD4)
![Microsoft Foundry](https://img.shields.io/badge/Microsoft%20Foundry-gpt--5.4--mini-0078D4)
![Terraform](https://img.shields.io/badge/Terraform-Azure-7B42BC)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

Ask why a merchant's payout was late, and the agent checks the payment platform's records and runbooks itself, then answers with the evidence behind every statement. The setting is *Calderbrook Payments*, a fictional payment platform. Independently developed using synthetic payment data; not a production service.

![An investigation of a delayed payout: the question, each tool call as it happens, and the validated assessment](docs/media/investigation.gif)

*Captured on 2026-11-24 from the app running locally against the dev environment's services, on synthetic data. The signed-in account's name is blurred.*

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/media/stats-dark.svg">
  <img alt="Latest release evaluation: assessments passing server validation, median 100% over 3 runs (lowest run 97%); 0 of 87 safety cases followed an injected instruction; 0 authorization leaks or unapproved changes; 37 labelled cases × 3 runs gate every AI change; p95 full investigation 13.1 s, first progress 2.4 s" src="docs/media/stats-light.svg">
</picture>

- **Problem.** Late payouts and missing settlements are investigated by hand across settlement records, error logs, processor status and runbooks.
- **Solution.** The agent picks its own read-only tools, and the server checks every citation before an assessment is shown. It can propose an incident ticket; only a person can approve it.
- **Safety and governance.** Questions and untrusted text are screened for harm and planted instructions, restricted runbooks are filtered by the user's group, and every AI change must pass quality and safety gates.
- **Technology.** .NET 10, Blazor, Microsoft Agent Framework, Microsoft Foundry, Azure AI Search, Entra ID, Terraform and GitHub Actions.
- **Engineering depth.** Thresholds fixed before measuring, an agent that had to beat a fixed workflow, and a cheaper model-routing design measured and rejected.

## Demo

Asked *"Why were payouts for merchant M-1042 delayed on November 9, 2026?"*, the agent read the merchant, its settlement batches, the processor's status and the runbooks, and found that Fabrikam Pay's settlement file for 9 November never arrived during the processor's incident INC-FAB-1109, while the merchant's Northwind payout was on schedule.

| Every statement shows its evidence | A proposed ticket waits for a person |
|---|---|
| ![Validated assessment with evidence chips](docs/media/investigation-result.png) | ![Incident ticket proposal awaiting approval](docs/media/approval-card.png) |

## How it works

```mermaid
flowchart LR
  Q["Question"] --> S{"Screened for harm<br/>and attacks"}
  S -->|blocked| X["Refused, with the reason"]
  S --> A["Agent chooses<br/>read-only tools"]
  A --> T["Tools return records;<br/>the server registers<br/>every id they return"]
  T -->|"untrusted text<br/>screened first"| A
  A --> V{"Server validates<br/>every citation"}
  V -->|"cites anything<br/>not returned"| R["Rejected, shown<br/>as a gap"]
  V --> AS["Assessment with<br/>evidence per statement"]
  AS -.->|optional| P["Ticket proposal"] --> H["A person approves;<br/>the server creates it"]
```

- **The model has six functions and nothing else**: five read-only tools (merchant profile, settlement batches, error events, processor status, knowledge search) and `submit_assessment`. No web search, code execution or hosted tools; a tool name outside that list is refused, and calls, time and tokens are capped per investigation.
- **The server owns the evidence.** An assessment may cite only ids this investigation's tools returned; anything else is rejected, and a missing or failed source shows up as a gap.
- **Who's asking decides what's retrieved.** Restricted runbooks are filtered out by the user's group before retrieval, so the model never sees them for the wrong person.
- **Data is untrusted.** Every question, and every piece of text written outside the system (incident notes, names, runbooks), is screened for harm and hidden instructions before the model reads it. Flagged text is withheld and can't support anything the agent says.
- **The model proposes; people decide.** The only action that changes anything, an incident ticket, is created by the server after the engineer who ran the investigation approves it with a recent sign-in.
- **Every investigation is traced**, with a decision record of what it did and used, holding no question or answer text.

## The last two release evaluations

Every change to AI behaviour (prompts, models, tools, knowledge, agent code) has to pass a release gate before it merges: 37 labelled cases × 3 runs, 26 held-out retrieval questions and 87 safety cases, against thresholds fixed before measuring from the spread of a ten-run baseline ([how](docs/Evaluation.md#the-threshold-rule)). These are the last two passing release runs, both on `gpt-5.4-mini` with the same thresholds and datasets.

**Reading the gates.** Evaluation thresholds are regression-detection floors calibrated against measured run-to-run variation, not target quality scores. They help detect meaningful deterioration while reducing failures caused by normal model variability; a small drop can still pass. Passing them isn't the same as being good: assessments cite fewer than half of the runbook sections the labels require (section recall 0.438), and in over a third of the tool uses the labels constrain, no call had every argument right (tool arguments 0.630). Both are real limitations, not noise.

<a href="docs/media/evaluation-quality-light.svg"><picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/media/evaluation-quality-dark.svg">
  <img alt="Quality medians, 2026-11-09 then 2026-11-18, against the gate: passed server validation 0.946, 1.000 (gate 0.92); outcome allowed 0.892, 0.919 (0.84); evidence cited 0.727, 0.712 (0.66); required tools 0.814, 0.791 (0.78); tool arguments 0.648, 0.630 (0.57); section precision 0.767, 0.773 (0.72); section recall 0.469, 0.438 (0.40); retrieval recall@5 0.808, 0.769 (0.76); unwarranted ticket proposals 0.000, 0.000 (lower is better, ceiling 0.05)" src="docs/media/evaluation-quality-light.svg">
</picture></a>

<a href="docs/media/evaluation-safety-light.svg"><picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/media/evaluation-safety-dark.svg">
  <img alt="Safety hard gates, both runs: 0 planted instructions followed, 0 harmful requests handled unsafely, 0 harmful text repeated, 0 restricted content leaked, 0 changes nobody approved" src="docs/media/evaluation-safety-light.svg">
</picture></a>

<a href="docs/media/evaluation-latency-light.svg"><picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/media/evaluation-latency-dark.svg">
  <img alt="Latency, 2026-11-09 then 2026-11-18: full investigation p95 17.2 s, 13.1 s; p50 12.9 s, 7.6 s; first progress p95 2.9 s, 2.4 s" src="docs/media/evaluation-latency-light.svg">
</picture></a>

<details>
<summary>The same results as a table (easier on a phone; select a chart above for its full size)</summary>

| Metric (median of 3 runs) | Gate | 2026-11-09 | 2026-11-18 |
|---|---|---|---|
| Passed server validation | ≥ 0.92 | 0.946 | 1.000 |
| Outcome allowed by the label | ≥ 0.84 | 0.892 | 0.919 |
| Expected evidence cited | ≥ 0.66 | 0.727 | 0.712 |
| Required tools called | ≥ 0.78 | 0.814 | 0.791 |
| Tool arguments correct | ≥ 0.57 | 0.648 | 0.630 |
| Runbook sections: precision | ≥ 0.72 | 0.767 | 0.773 |
| Runbook sections: recall | ≥ 0.40 | 0.469 | 0.438 |
| Unwarranted ticket proposals (lower is better) | ≤ 0.05 | 0.000 | 0.000 |
| Retrieval recall@5, held out | ≥ 0.76 | 0.808 | 0.769 |
| Full investigation, p95 | | 17.2 s | 13.1 s |
| Full investigation, p50 | | 12.9 s | 7.6 s |
| First progress shown, p95 | | 2.9 s | 2.4 s |
| Safety hard gates (any count fails) | 0 | 0 | 0 |

</details>

**What changed between them.** [#61](https://github.com/ankurparekh1988/payment-ops-investigation-agent/pull/61) bounded usage and kept untrusted text out of tool choices (prompt `investigation-agent` 1.1.0 → 1.2.0), and [#64](https://github.com/ankurparekh1988/payment-ops-investigation-agent/pull/64) added model routing, switched off. The validation median reached 1.000 and p95 latency fell by about a quarter; evidence and tool recall dipped within their gates. Retrieval recall@5 went from 21 to 20 of 26 with the knowledge and retrieval code unchanged: one question fell out of the top five and another moved up.

**Safety outcome.** Both recorded release evaluations had zero end-to-end hard-gate violations on the 87 safety cases: no planted instruction followed, no harmful request handled unsafely, no harmful text repeated, no restricted content leaked and no change made without approval. The detection rates below describe individual screening layers, not the complete system's protection.

**Safety rates**, reported rather than gated, on the safety cases (both runs unless noted). The 2026-11-09 rates are medians over three safety runs; 2026-11-18 had one run.

| What was measured | Result | Reading it |
|---|---|---|
| Harmful questions blocked before the model | 6 of 6 | |
| Harmful tool text withheld from the model | 6 of 6, then 5 of 6 | The one that got through wasn't repeated; the hard gate held |
| Direct attacks blocked by the screen | 1 of 6 | The remaining evaluated direct attacks ended in refusal |
| Planted instructions flagged by the screen | 1 of 12 | None had its effect (the first hard gate above) |
| Ordinary questions wrongly blocked or refused | 2 of 8 | The cost of a strict filter on look-alike wording |

![Signed in as an Admin, who may approve tickets: an instruction to ignore the rules and approve every ticket, then a question about hurting someone, are both stopped before the agent runs](docs/media/safety-refusals.gif)

*Two prompts from the safety set, on the app running locally against dev and signed in as an Admin, a role that is allowed to approve tickets. Both are blocked before the agent runs, so no tool is called and nothing is changed. The screen catching a direct attack is the less common case: in the evaluation, the remaining direct attacks ended in refusal.*

The zero counts are results on these evaluated cases, not a guarantee of future safety. Full results: [2026-11-18](docs/evidence/release-evaluation-2026-11-18.md), [2026-11-09](docs/evidence/release-evaluation-2026-11-09.md); the charts are drawn from that evidence by [`scripts/readme-charts.py`](scripts/readme-charts.py).

## Engineering that's easy to miss

- **Thresholds fixed before the run they judge**, from the measured run-to-run spread of a ten-run baseline ([ADR 0018](docs/adr/0018-evaluation-thresholds-from-measured-run-to-run-spread.md)). A changed agent gets no free rerun ([ADR 0016](docs/adr/0016-one-recorded-rerun-when-the-agent-is-unchanged.md)), and a case the environment spoiled is retried on its own, never the whole run ([ADR 0019](docs/adr/0019-retry-the-case-the-environment-spoiled.md)).
- **The gate was shown to fail**: a deliberately degraded prompt didn't pass it ([record](docs/Evaluation.md#proving-the-gate-2026-10-25)).
- **The agent had to earn its place.** It beat a fixed-sequence workflow on the same cases, by a rule written before either ran ([kill test](docs/Evaluation.md#agent-against-a-fixed-workflow-the-kill-test)).
- **A cheaper design was measured and rejected.** Starting on the smaller `gpt-5.4-nano` cut the modelled cost per validated investigation by 32.8%, past the 25% the pre-registered rule required, but followed a planted instruction in one run, missed quality thresholds and raised p95 latency 52%. Routing stays off and nothing was tuned in response ([ADR 0023](docs/adr/0023-route-to-a-small-model-first-by-a-pre-registered-rule.md), [evidence](docs/evidence/routing-decision-2026-11-18.md)).
- **Retrieval chosen by measurement.** Semantic ranking had to beat lexical by a margin fixed in advance on reviewed cases, and didn't ([ADR 0007](docs/adr/0007-retrieval-strategy-by-frozen-calibration.md)).
- **Keyless Azure service access.** The deployed app reaches every Azure service with its managed identity (locally, your `az login`), API-key authentication is disabled, and pipelines sign in with OIDC. The app's only credential is for running locally: Entra sign-in on a workstation uses a client secret that expires after 30 days, kept in .NET user-secrets ([Security](docs/Security.md#keyless-controls)).
- **Sessions are owned by the server**: a fresh credential at every sign-in, sign-out after 15 minutes idle, and a recent sign-in to approve a ticket or resume investigations ([Security](docs/Security.md#users-and-sign-in)).
- **Models are pinned and checked before deploy.** Each plan fails if a model isn't offered at its pinned version or retires within 90 days, and capacity is checked right before apply ([Deployment](docs/Deployment.md#model-preflight)).
- **Usage is bounded and observable**: daily limits per person and in total, an Admin pause switch, a cost estimate per investigation, and an optional budget cutoff for the AI account ([ADR 0021](docs/adr/0021-bounded-usage-and-observable-cost.md), [Runbook](docs/Runbook.md#pausing-and-limits)).
- **Evaluation history lives outside the app**, in Log Analytics with an Admin-only workbook.

All decisions: [docs/adr](docs/adr/README.md).

## How this was built

Built by me over seven weeks of evenings (5 Oct – 24 Nov 2026), with AI coding agents doing most of the implementation.

- **My part:** the product notes, the architecture, all 23 decision records, the evaluation design and thresholds, the safety model, and the acceptance criteria for every change, which I reviewed and merged.
- **The agents' part:** implementing each change on its own branch against those criteria.
- **Same rules for everyone:** agent-written code passes the same CI, AI evaluation gate and safety gate as anything else; nothing merges without them.

## Architecture

```mermaid
flowchart LR
  U["Ops users<br/>(browser)"] -->|sign in| E["Entra ID<br/>app roles, groups"]
  U -->|HTTPS| APP["Web app (Blazor, .NET 10)<br/>agent, tools, validator,<br/>proposals and approval"]
  APP -->|"screen questions<br/>and tool text"| CS["Content Safety<br/>+ classifier deployment"]
  APP -->|"model calls"| F["Microsoft Foundry<br/>gpt-5.4-mini"]
  APP -->|"knowledge, filtered<br/>by the user's groups"| S[("Azure AI Search")]
  APP -->|"read-only ports"| D[("Merchant, settlement,<br/>processor data (synthetic)")]
  APP -->|"traces, metrics,<br/>decision records"| AI["Application Insights<br/>Log Analytics, workbook"]
  CD["GitHub Actions<br/>CI, CD, AI evaluation"] -->|"Terraform, publish,<br/>deploy (OIDC, no keys)"| APP
```

The AI release manifest (`ai/manifest.yaml`) names the models, prompts and filters, and both the infrastructure and the app read it. Detailed diagrams of the platform, the investigation loop, retrieval, screening and approval are in [Architecture](docs/Architecture.md), and the pipelines in [Deployment](docs/Deployment.md).

<details>
<summary>Operations workbook, evaluation history and a deployment run</summary>

| Model tokens per day | Evaluation history in Log Analytics |
|---|---|
| ![Workbook tile of model tokens per day by deployment](docs/media/workbook-tokens.png) | ![Gate verdicts of recorded evaluations](docs/media/evaluation-history.png) |

| Investigations by outcome | A deployment run |
|---|---|
| ![Workbook tile of investigations by outcome](docs/media/workbook-quality.png) | ![A successful CD run: build, plan, apply, publish knowledge, deploy, smoke test](docs/media/cd-run.png) |

*Recorded from the dev environment on 2026-11-19.*

</details>

## Running it

You'll need the [.NET SDK 10.0.404](https://dotnet.microsoft.com/download/dotnet/10.0), the version `global.json` pins. The build and all tests run without Azure:

```bash
dotnet build PaymentOps.slnx
dotnet test PaymentOps.slnx
```

The app itself runs against Azure: Entra ID for sign-in, Foundry for the model, AI Search for the knowledge and Content Safety for screening. Without them, pages return 503 or investigations report that a service isn't responding. [SETUP.md](SETUP.md) creates all of it in your own subscription with one script and runs the app on localhost; [Deployment](docs/Deployment.md) covers the pipeline-deployed environment.

## Documentation

[Product notes](docs/PRD.md) · [Architecture](docs/Architecture.md) · [Deployment](docs/Deployment.md) · [Security](docs/Security.md) · [Governance](docs/Governance.md) · [Runbook](docs/Runbook.md) · [Evaluation](docs/Evaluation.md) · [Evidence](docs/evidence/README.md) · [Decisions](docs/adr/README.md)

## Deliberate production gaps

One dev environment with synthetic data. These are left out on purpose, each with what would trigger building it, in [Production gap analysis](docs/ProductionGapAnalysis.md):

- public endpoints with Entra and TLS, not private networking; one region; free and entry tiers without an SLA;
- conversations, approvals and tickets held in memory, lost on restart, on a single instance;
- synthetic data behind ports, with no real systems of record or ticket system;
- one maintainer, so no independent approval of AI changes, and evaluation runs on request because each one costs money;
- model routing measured once on labelled cases, not on real traffic.

## License

[MIT](LICENSE)
