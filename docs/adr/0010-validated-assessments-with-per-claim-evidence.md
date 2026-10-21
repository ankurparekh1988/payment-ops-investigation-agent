# 0010. Validated assessments with per-claim evidence and server-observed gaps

- **Status:** Accepted
- **Date:** 2026-10-21

## Context

An investigation's answer mixes three kinds of support: records from systems of record (a payout, a file, an incident), facts computed from those records (a missing file, a payment's expected batch, a count of errors), and knowledge sections that explain them. People have to be able to see which statement rests on what, and the model can't be trusted to keep to what it was actually given, or to report honestly what it couldn't check.

[ADR 0006](0006-grounded-answers-with-a-server-owned-evidence-registry.md) solved this for one question and one answer with inline `[Sn]` markers, and noted that a citation list per claim would suit the agent better.

## Decision

**Per-statement evidence lists, checked against evidence only server code registered, with labels, outcome caps and gaps decided by the server.**

- The assessment has a summary, claims, a root cause and next steps. Each statement lists its evidence ids and citation ids; ids never appear in its text.
- Server code registers evidence as tools return it, from typed results: knowledge sections numbered `S1`… across the investigation, records by type and id, and computed facts with the exact records they came from. A count is supported by the group that counted every event: a statement citing some of a group's events must cite the group too, so the ids shown under a list cap can't stand in for it.
- The summary and every claim need at least one id, and every id must be in the investigation's evidence. A failure goes back to the model, listed by rule, for one correction with only `submit_assessment` available; a second failure shows nothing.
- A root cause is labelled *assessment* only with both data and knowledge behind it; otherwise *hypothesis*. The model doesn't choose the label.
- Gaps (a source unavailable or too slow, a list cut short, a check the user wasn't allowed, a limit reached) are recorded as they happen and shown in fixed words. The model doesn't report gaps, so it can't leave one out. Gaps cap confidence, a stopped investigation is incomplete, and with every data source failed there's no root cause, whatever was submitted.

## Consequences

- Every statement a person sees traces to something this investigation's tools returned, and the support is shown per statement, not for the answer as a whole.
- Some correct assessments fail because the model slipped on an id. One correction is allowed because an investigation is expensive to throw away; grounded answers, which are cheap, are never retried.
- A statement with nothing a tool returned to point at can't be shown. That includes a correct "there's no such merchant", because a lookup that finds nothing registers no evidence.
- A statement that cites some of a counted group's events must cite the group too. The rule doesn't read the text, so it also applies when no count is stated; citing the group costs nothing.

## Alternatives considered

- **Inline markers, as for grounded answers.** Familiar, but each new evidence kind would extend the marker grammar the renderer trusts, and per-statement support would have to be parsed back out of text.
- **The model reports its own gaps and confidence.** Simpler, but a model that left out a failed source would keep a confidence it hadn't earned.
- **Structured output on the final message instead of a tool.** Agent Framework supports it alongside tools. A tool makes the end of the investigation an explicit step, keeps validation feedback inside the loop, and is what the investigation view will render.
