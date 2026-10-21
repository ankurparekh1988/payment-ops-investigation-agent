# 0009. One investigation agent on Agent Framework, with our own limits

- **Status:** Accepted
- **Date:** 2026-10-21

## Context

Working out why a payout was late means following a trail: which processors a merchant uses, which batch a payment belongs in, whether that batch's file arrived, whether the processor had an incident covering its cut-off, what our own error logs say, and what the runbook says to do. What to look up next depends on what came back: a risk hold ends the settlement checks, a missing file leads to the processor's incidents, two files for one batch lead to comparing their payments, and the rule id on a hold goes into the knowledge search.

A fixed sequence (fetch everything for a merchant and a date, then make one model call) would also answer the four scenarios this system is built around. It gets weaker when the question doesn't start from a merchant and a date, when what to search for depends on what was found, and as scenarios grow. The model is also least reliable at exactly what the scenarios' distractors test: time zones, settlement-day counting and comparing sets of payments.

## Decision

**One agent, with five read-only tools that compute facts and limits enforced in our own code.**

- One `ChatClientAgent` from Microsoft Agent Framework, in the app's process, over the existing keyless chat client. Its tool loop is Microsoft.Extensions.AI's function-invoking client, configured to fail closed: one call at a time, no exception text to the model, an exception or an unknown tool ends the loop instead of being retried.
- Call, repeat, correction, per-tool and per-investigation limits are enforced by our own guard around every tool. The framework's iteration cap is only a backstop above them. No business rule depends on Agent Framework types, so replacing it is a contained change.
- Tools return facts, never conclusions. Date, time-zone and set arithmetic is done in code, and the agent interprets and correlates the results.
- Each tool checks the caller's role itself, so it's safe on its own, and the agent is offered only the tools the user may use.
- The agent finishes with a `submit_assessment` tool, validated against the investigation's evidence ([ADR 0010](0010-validated-assessments-with-per-claim-evidence.md)).
- Reasoning effort is none to start, set in code; low is the alternative to measure if tool choice turns out to need it.
- Each investigation is a single question with no carried history.

## Consequences

- The model chooses the path; the facts it reasons over are computed and testable without a model.
- Computed facts repeat runbook rules in code. Their tests quote the runbook sentences, so a change to a runbook shows which code must follow.
- Agent Framework releases often. It's pinned like any dependency and upgraded deliberately.
- Whether the agent earns its place over a fixed sequence is argued here, not yet measured. The evaluation suite will run a fixed-sequence baseline with the same tools on the same cases, with the rule for what a tie means written down before the run.

## Alternatives considered

- **A fixed sequence and one model call.** Faster and fully deterministic, and it would handle the seeded cases. It can't follow a finding into the next question, and it's kept as the baseline the agent has to beat.
- **Microsoft.Extensions.AI's function-invoking client on its own.** Equally capable; Agent Framework adds a named agent and session handling that the conversational view will use, at the cost of one more pinned package.
- **Foundry Agent Service.** Hosted agents move the loop and its state out of the app, which complicates per-user authorization and adds a service this design doesn't need.
- **Tools that return raw records only.** Purer agent reasoning, but the time-zone and set-comparison distractors would fail by design and look like model-quality problems.
