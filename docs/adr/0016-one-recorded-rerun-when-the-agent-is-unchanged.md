# 0016. One recorded rerun of a failed evaluation when the agent under test is unchanged

- **Status:** Accepted
- **Date:** 2026-11-02

## Context

[ADR 0012](0012-evaluation-thresholds-from-a-pre-registered-rule.md) sets each threshold one item below the worst of three baseline runs, so that run-to-run noise of one item passes and a loss of two or more fails. Its rule forbids rerunning a failure until it passes, and records a failure on variance as a finding.

In the last three pull requests the model was given the same prompt, `investigation-agent` 1.1.0 on `chat-blue` (`gpt-5.4-mini` 2026-03-17), with the same tools, knowledge and data. #42 changed how a refusal is validated after the model answers. #43 added the question screen, which ends some questions before the agent runs; its harm check on tool text withheld none of the text these cases read, checked against every dataset string and knowledge section. #44 only declared a model deployment that nothing calls. On the two metrics nearest their thresholds, the per-run values were:

| Pull request | Tool argument accuracy (54 labels), runs | Median | Evidence coverage (66 items), runs | Median | Gate |
|---|---|---|---|---|---|
| #42 | 0.593, 0.630, 0.630 | 0.630 | 0.697, 0.727, 0.697 | 0.697 | passed |
| #43 | 0.648, 0.556, 0.648 | 0.648 | 0.758, 0.697, 0.742 | 0.742 | passed |
| #44 | 0.556, 0.556, 0.704 | 0.556 | 0.682, 0.621, 0.758 | 0.682 | failed |

The baseline's three runs spanned 1 label (35 to 36) and 2 items (47 to 49); the thresholds need 34 and 46. Since then, on an agent that didn't change, tool argument accuracy has ranged over 8 labels (30 to 38) and evidence coverage over 9 items (41 to 50), so a median of three runs can land either side of the threshold. #44 failed for that reason alone: across #43 and #44 the same labels flipped in both directions, and Azure throttled fewer requests during #44's run (12) than during #43's (19).

A gate that can fail an unchanged agent blocks changes that don't touch the agent, such as infrastructure a later change will use, and invites bypassing the required check, which would remove the control for every change.

## Decision

**A failed evaluation may be rerun once, and only when the agent under test is unchanged. The rerun's result stands. Thresholds, labels and the formula don't change.**

- **The failure is recorded first,** in [Evaluation.md](../Evaluation.md#recorded-failures-and-reruns): the pull request, the run, each failing metric with its per-run values, and why the agent under test is unchanged. Nothing is rerun before that record is merged or is part of the pull request being rerun.
- **The agent under test is unchanged** when, against the last evaluation that passed on `main`:
  - the report names the same prompts and versions (a prompt's version must change with its body) and the same chat deployment, model, version and deployment type;
  - the change touches none of: the agent, its tools, the knowledge corpus or how it's built and published, the demo data, the domain model, any prompt, or the manifest's chat, embedding or retrieval settings;
  - any other change in a behaviour path is shown in the record not to reach what the agent is given. Declaring a deployment that nothing calls, or a field the agent doesn't read, qualifies.
- **Once.** A second failure stands and is recorded. The change then waits for a decision on the gate, or is changed itself.
- **Never for a change to AI behaviour.** A pull request that changes what the agent is given is judged by its single evaluation, as before.
- **The same rule covers gate-policy pull requests,** which evaluate `main`'s agent under the new policy.

The rule was written after #44 failed and is applied to #44 first. It doesn't move a threshold or change what passes, and the rerun counts whatever it shows.

## Consequences

- A change that doesn't touch the agent is blocked only if both of its evaluations land low, so infrastructure and policy changes get through on evidence instead of a bypass.
- The underlying problem remains: a threshold one item below the worst baseline run sits inside the measured noise of these two metrics. Fixing it, with more runs per evaluation or a margin taken from measured run-to-run variance, changes how thresholds are derived. It needs its own ADR and applies at the next baseline, never to a pending change.
- A behaviour change gets no rerun, so it can still fail on variance. That's deliberate: a rerun there would let an unlucky regression through on a second draw.
- Whether the agent under test is unchanged is a judgement written in the record and reviewed in the pull request, not a check the workflow enforces.

## Alternatives considered

- **Bypass the required check for changes that don't touch the agent.** Rejected: it removes the gate whenever someone decides a change is safe, without evidence.
- **Lower or widen the thresholds now.** Rejected: thresholds are never changed after seeing a result, and this one would be.
- **Rerun until it passes.** Rejected: with enough draws any change passes.
- **Skip the evaluation for changes outside the agent.** The classifier decides that from paths, and `ai/**` and the release loader are behaviour paths because most changes there do reach the agent. Narrowing them is possible, but per path it can't tell a declared-but-unused deployment from a switched one.
