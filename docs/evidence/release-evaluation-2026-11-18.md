# Release evaluation, 2026-11-18

Sanitized from the release gate's reports for the change that added model routing, switched off; this run was also the routing measurement's control. See [README](README.md) for what was omitted.

[Evaluation run](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/runs/44307364386) at merge commit `23a88d69341d5ae54a6e3327f1ae9ce00dae1e8d` (tree `ca76c9a7a3c3bed0f06cb29ccafe22cdfc6ba6c9`), routing `off`, chat `gpt-5.4-mini` 2026-03-17.

Investigations: **passed**, 37 cases × 3 runs, gated on the median.

| Metric | Threshold | N | Runs | Median |
|---|---|---|---|---|
| validated_result_rate | >= 0.92 | 37 | 0.973, 1.000, 1.000 | 1.000 |
| allowed_outcome_rate | >= 0.84 | 37 | 0.919, 0.919, 0.919 | 0.919 |
| required_tool_recall | >= 0.78 | 86 | 0.791, 0.814, 0.779 | 0.791 |
| tool_argument_accuracy | >= 0.57 | 54 | 0.630, 0.685, 0.611 | 0.630 |
| expected_evidence_coverage | >= 0.66 | 66 | 0.712, 0.742, 0.712 | 0.712 |
| expected_section_precision | >= 0.72 | 31 | 0.773, 0.757, 0.774 | 0.773 |
| expected_section_recall | >= 0.40 | 32 | 0.438, 0.438, 0.438 | 0.438 |
| proposal_false_positive_rate | <= 0.05 | 23 | 0.000, 0.000, 0.000 | 0.000 |
| proposal_recall (reported) | none | 14 | | 0.571 |

Hard gates across all runs: 0 authorization leaks, 0 unauthorized state changes.
Latency: full investigation p50 7612 ms, p95 13059 ms; time to first progress p50 1532 ms, p95 2382 ms.

Retrieval: **passed**, recall@5 0.769 against >= 0.76 over 26 held-out questions; MRR 0.731 (reported).

Safety: **report-only** (1 run on a pull request; its hard gates fail the check, its rates are reported), 87 cases. Violations: 0 injections followed, 0 unsafe handling, 0 harmful text repeated, 0 authorization leaks, 0 unauthorized state changes.

## Source reports

SHA-256 of the unmodified reports (kept privately; also the run's workflow artifacts while GitHub retains them):

- `investigations`: `sha256:9aac06ecb9bbdca708618250a637ee6cc294a9bff0a41640520afce9f5ee7b85`
- `retrieval`: `sha256:3edc8df4b218710925dd161433bf829fde182a1f986c57948380bd51979762e8`
- `safety`: `sha256:15e1e42092009bcbbdabc2a4ae63f5a7b604c6b5baa61504d1961117d5158e2e`

Omitted: absolute cost values and prices (USD); per-case transcripts, tool arguments and retrieved sections; service endpoints; price source URLs; run start times.
