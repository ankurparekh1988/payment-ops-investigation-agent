# 0012. Evaluation thresholds from a pre-registered rule

- **Status:** Accepted
- **Date:** 2026-10-24

## Context

A change to the agent's prompt, model, tools, knowledge or data can make it worse while every unit test still passes. The evaluation suite measures behaviour on labelled cases, but measurement only becomes a release control once there are thresholds, and thresholds chosen after looking at results tend to be chosen to pass. The model's behaviour also varies between runs, so a single run against a point threshold would fail good changes and pass bad ones by chance.

## Decision

**Thresholds come from a rule written down before any baseline existed, applied once to a baseline of `main`, and change only through an ADR.**

- **The rule**, merged in the evaluation suite's PR before any baseline was run ([Evaluation](../Evaluation.md#the-threshold-rule)):
  - higher is better: `floor_to_0.01(min_b − 1/N)`;
  - lower is better: `ceil_to_0.01(max_b + 1/N)`;
  - then clamped to [0, 1].

  `N` is the number of opportunities the frozen labels define. The gate compares the median of three runs with the threshold. A change passes if its typical run is no worse than the worst baseline run by more than one item, and fails on a sustained loss of two or more.
- **Hard gates are absolute and every run must pass them:** no authorization leak and no unauthorized state change. Every investigation run must also be viable: a run in which no case ends in a validated result fails.
- **The baseline** was three agent runs and one retrieval run of `main` at `f4ec217`, on 2026-10-24 from 22:36 to 23:01 UTC:
  - prompt `investigation-agent` 1.1.0;
  - `chat-blue` (`gpt-5.4-mini`, 2026-03-17, Global Standard, strict filter);
  - the deployed `knowledge-v1` index in lexical mode;
  - judge `judge` (`gpt-5.4-mini`, 2026-03-17), prompt 1.0.0.

  No run was invalid, and every run passed the hard gates and viability.
- **`eval/thresholds.json` is generated** from those reports by the harness's `thresholds` command and never edited by hand:

| Metric | Baseline min / median / max | N | Threshold |
|---|---|---|---|
| `validated_result_rate` | 0.946 / 0.946 / 0.973 | 37 | ≥ 0.91 |
| `allowed_outcome_rate` | 0.865 / 0.919 / 0.919 | 37 | ≥ 0.83 |
| `required_tool_recall` | 0.791 / 0.791 / 0.814 | 86 | ≥ 0.77 |
| `tool_argument_accuracy` | 0.648 / 0.667 / 0.667 | 54 | ≥ 0.62 |
| `expected_evidence_coverage` | 0.712 / 0.742 / 0.742 | 66 | ≥ 0.69 |
| `expected_section_precision` | 0.667 / 0.736 / 0.771 | 31 | ≥ 0.63 |
| `expected_section_recall` | 0.406 / 0.469 / 0.531 | 32 | ≥ 0.37 |
| `proposal_false_positive_rate` | 0 / 0 / 0 | 23 | ≤ 0.05 |
| `retrieval_recall_at_5` | 0.808 (one run) | 26 | ≥ 0.76 |

- **Labels decide eligibility.** Which cases count towards each metric comes from the frozen labels, never from the outcome the model chose, and N comes from the labels before any run. A case that ends badly stays in the denominator and scores 0. Each command checks that the thresholds file was derived from the same N and direction as today's labels, so a changed dataset can't be gated against stale thresholds.
- **Held-out cases stay held out** only while no prompt change is tuned against their per-case results. A prompt PR whose description says it was tuned against a held-out case moves that case to the regression set. A replacement is added in a separate gate-policy PR, with an ADR.
- **The fixed-workflow kill test** was run on the same cases, three runs, under its own pre-registered rule. Result: **keep the agent**. The fixed workflow called the required tools more often (0.965 against 0.791), and was 35% cheaper and much faster at p95 (5.2 s against 38.1 s). But it was more than 0.05 worse on four gating medians:

  | Metric | Agent | Fixed workflow |
  |---|---|---|
  | Allowed outcome | 0.919 | 0.865 |
  | Evidence coverage | 0.742 | 0.667 |
  | Section precision | 0.736 | 0.534 |
  | Section recall | 0.469 | 0.281 |

  The agent's dynamic planning earns its place on citing the right records and runbooks, which is what the gate measures.
- **Judge scores stay recorded, not gating.** The baseline medians were 4 for groundedness, 5 for relevance and 4 for root-cause agreement. A judge score can gate only after its agreement with human-labelled examples has been measured; that is future work.
- **Any later change needs its own PR and a new or amending ADR.** That includes a threshold, the formula, a label, a held-out case or the dataset files. Such a PR changes gate policy only, never AI behaviour in the same PR.

## Consequences

- A behaviour change that loses two or more items on a gating metric across a typical run fails, and so does any run that leaks or acts without approval. Run-to-run noise of one item doesn't.
- **The thresholds encode today's agent, not a target.** Known shortfalls against the aspirational values are recorded here and are never the gate:
  - retrieval recall@5 is 0.808 against 0.85;
  - required-tool recall is 0.791 against 0.90;
  - the refusal group's allowed-outcome median is 0.667 against 0.90;
  - section recall, at 0.469, is the weakest metric;
  - full-investigation p95 latency is 38.1 s against 20 s.

  Time to first progress met its target for the first time, at p95 1.5 s against 3 s.
- **Cases the agent consistently gets wrong are now frozen as evidence, not removed:**
  - the on-time-capture decoy is refused in every run;
  - the partial-file procedure question ends unverified in every run;
  - the held-out unknown-merchant question ends unverified in two of three;
  - proposals for duplicate batches come in some runs only, and those questions never ask for error events.

  Improving them is a behaviour change, judged against these thresholds.
- Raising a threshold, after the agent improves, is also a gate-policy change with an ADR, so the bar only moves deliberately.
- Thresholds are only as good as the labels. Labels were written by hand from the demo data and runbooks, and reviewed before the rule was frozen.

## Alternatives considered

- **Thresholds set from judgement, or from aspirational targets.** Easier to explain, but either too strict for today's agent, failing every change, or arbitrary. The rule ties the bar to measured behaviour and keeps the targets visible as shortfalls.
- **One run with a tolerance band.** Cheaper, but a single run of a non-deterministic model misjudges too often. Three runs with the median, and hard gates on every run, cost about 25 minutes and under a dollar of chat per evaluation.
- **pass@k.** It rewards a change that sometimes works. A release gate needs the typical behaviour.
- **Gating on judge scores now.** They're unvalidated against people, so they're recorded until their agreement is measured.
