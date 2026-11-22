# Release evaluation, 2026-11-09

Sanitized from the release gate's reports for the recorded rerun of [#58](https://github.com/ankurparekh1988/payment-ops-investigation-agent/pull/58), which runs the safety evaluation in parallel with the investigations. See [README](README.md) for what was omitted.

[Evaluation run](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/runs/42624248665) at pull request head `a8c1a8d` (test merge commit `3405303f243be25f04f26531c1c318f73b05b54d`), chat `gpt-5.4-mini` 2026-03-17, prompts `investigation-agent` 1.1.0, `harm-screen` 1.0.0, `tool-text-harm-screen` 1.0.0. These reports predate report identity, so they name the commit but not the tree or input hashes.

Investigations: **passed**, 37 cases × 3 runs, gated on the median.

| Metric | Threshold | N | Runs | Median |
|---|---|---|---|---|
| validated_result_rate | >= 0.92 | 37 | 0.946, 1.000, 0.946 | 0.946 |
| allowed_outcome_rate | >= 0.84 | 37 | 0.892, 0.946, 0.892 | 0.892 |
| required_tool_recall | >= 0.78 | 86 | 0.814, 0.802, 0.814 | 0.814 |
| tool_argument_accuracy | >= 0.57 | 54 | 0.685, 0.611, 0.648 | 0.648 |
| expected_evidence_coverage | >= 0.66 | 66 | 0.758, 0.697, 0.727 | 0.727 |
| expected_section_precision | >= 0.72 | 31 | 0.767, 0.791, 0.720 | 0.767 |
| expected_section_recall | >= 0.40 | 32 | 0.406, 0.469, 0.500 | 0.469 |
| proposal_false_positive_rate | <= 0.05 | 23 | 0.000, 0.000, 0.000 | 0.000 |
| proposal_recall (reported) | none | 14 | | 0.571 |

Hard gates across all runs: 0 authorization leaks, 0 unauthorized state changes.
Latency: full investigation p50 12939 ms, p95 17163 ms; time to first progress p50 2131 ms, p95 2927 ms.

Retrieval: **passed**, recall@5 0.808 against >= 0.76 over 26 held-out questions; MRR 0.731 (reported).

Safety: **report-only** (3 run on a pull request; its hard gates fail the check, its rates are reported), 87 cases. Violations: 0 injections followed, 0 unsafe handling, 0 harmful text repeated, 0 authorization leaks, 0 unauthorized state changes.
Safety rates (reported, not gated): attack_withheld_rate 0.083 (N 12); harmful_text_withheld_rate 1.000 (N 6); harmful_blocked_rate 1.000 (N 6); attack_blocked_rate 0.167 (N 6); benign_false_positive_rate 0.250 (N 8); benign_withheld_rate 0.000 (N 8).

## Source reports

SHA-256 of the unmodified reports (kept privately; also the run's workflow artifacts while GitHub retains them):

- `investigations`: `sha256:354e16eeedb5d97ba189a76533369bdddb41a4409e0c7ecefd498ceebfe256a7`
- `retrieval`: `sha256:6577fae66f0c76aa15915bc665f7e1b8673179ae2e2e039db6b8b3c5455b126c`
- `safety`: `sha256:9d22f4702f111f13e5d82c56918a8da089c145d63241b2ff39316c79dba64652`

Omitted: absolute cost values and prices (USD); per-case transcripts, tool arguments and retrieved sections; service endpoints; price source URLs; run start times.
