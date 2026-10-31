# Governance

What the agent is for, what it isn't, and how each Responsible AI principle is met by a control in this repository rather than by a promise.

## Transparency note

**What it does.** An operations person asks why a merchant's money didn't move. The agent reads merchant, settlement, processor and error data and the operations knowledge through read-only tools, and returns an assessment: an outcome, a summary, claims, a root cause labelled *Assessment* or *Hypothesis*, a confidence, gaps and next steps. Every statement lists the records, computed facts and knowledge sections behind it, checked by the server against what this investigation's tools returned. When a runbook says to raise an incident, it can propose a ticket for an Operations Engineer to approve.

**Who it's for.** Contoso's internal operations, engineering, admin and risk teams. Merchants never use it.

**What it isn't for.** Deciding anything about a merchant, such as holds, limits, fees or offboarding; it has no tool that changes anything, and only a person can create a ticket. It isn't a source of record: the systems it reads are. It doesn't answer questions outside payments operations; it refuses them.

**Limitations.**

- It can be wrong in ways validation can't catch: a claim can cite real records and still draw the wrong conclusion. The label, confidence and gaps say how far to trust it; a hypothesis is a lead, not a finding.
- It sees only what its tools return and the user may read. A data source that's down, slow or cut short is shown as a gap, and the outcome is capped accordingly.
- Text written outside the system is screened before the model reads it, and what's flagged or can't be checked is withheld. Withholding can hide something relevant; the gap says so, and the source page shows the original to an authorized person.
- The content filter can block legitimate operational language; the person then sees a fixed notice and can rephrase.
- All data here is synthetic, and quality is measured on labelled cases, not on real incidents ([Evaluation](Evaluation.md)).

## Principles and controls

| Principle | How it's met here | Where |
|---|---|---|
| Fairness | The agent decides nothing about a merchant; it explains money movement from records, and every statement cites them. Whether answers differ for otherwise identical merchants isn't tested yet ([ProductionGapAnalysis](ProductionGapAnalysis.md)) | [Architecture](Architecture.md#assessments) |
| Reliability and safety | Server-side validation of every statement; limits on calls, repeats, corrections and time; the tool loop fails closed; untrusted text screened and withheld when flagged; a blocked question ends with a fixed notice; behaviour changes pass a frozen evaluation gate before merge, and `main` is evaluated weekly | [Architecture](Architecture.md#limits), [Security](Security.md#injection-and-content-safety), [Evaluation](Evaluation.md) |
| Privacy and security | Entra sign-in and app roles; Restricted knowledge filtered by the user's access before retrieval; no keys anywhere; logs hold ids, counts and timings, never questions, tool output or model text; ticket text checked for bank details | [Security](Security.md) |
| Inclusiveness | Plain-language outcomes and fixed wording for gaps and notices. The interface hasn't had an accessibility review yet; it's part of the interface redesign | — |
| Transparency | Each statement shows its evidence and sources; the root cause is labelled; what couldn't be checked or was withheld is listed; refusals and blocks use fixed server notices; prompts and models are versioned in the AI release manifest | [Architecture](Architecture.md#investigation-view), [ADR 0004](adr/0004-ai-release-manifest.md) |
| Accountability | Only the engineer who ran the investigation can approve its ticket, re-checked at the click; decisions are recorded in ADRs; evaluation history is kept in Log Analytics; every result carries an investigation id that ties together its logs, proposal and ticket | [ADR 0011](adr/0011-the-model-proposes-the-server-executes.md), [Runbook](Runbook.md) |
