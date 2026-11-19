# Evidence

Published derivations of measurements that the rest of the documentation cites. Each is generated from machine-written reports and checked against them. None is a raw report: the raw reports hold prices, per-case transcripts and service endpoints, and are kept privately. Each file lists the SHA-256 of the reports it came from.

## Routing decision, 2026-11-18

[`routing-decision-2026-11-18.md`](routing-decision-2026-11-18.md) (readable) and [`routing-decision-2026-11-18.json`](routing-decision-2026-11-18.json) (the same content as data): the measurement of the small-model-first cascade under [ADR 0023](../adr/0023-route-to-a-small-model-first-by-a-pre-registered-rule.md), recorded in [Evaluation](../Evaluation.md#measurement-2026-11-18-rejected). The verdict was **reject**, and routing stays off.

They were derived from four reports: the `routing-decision` output, the control's investigation report from the release gate run, and the cascade's investigation and safety reports. A local script derived them, and it fails unless:

- the JSON and Markdown state the same verdict, cost reduction, latency ratio and metric medians;
- each median equals the one in the source report;
- the index fingerprint, pricing hash and thresholds hash agree across the control and cascade reports;
- the thresholds hash equals that of `eval/thresholds.json` in this repository;
- the control, cascade and safety reports name the same Git tree;
- no price, cost value or service endpoint remains.

**Kept:**
- the verdict, each step of the rule with its findings, and every gating metric per run against its threshold and the control's median;
- cost as a percentage reduction per validated investigation;
- p95 latency in milliseconds and as a ratio;
- escalation and first-attempt counts;
- the commits, the shared Git tree and whether the checkout was clean;
- the hashes of the datasets, prompts, release manifest, pricing file and thresholds, the model identities, and the index name, document count and fingerprint;
- the SHA-256 of each source report.

**Omitted:**
- absolute cost values and prices; the cost step's finding shows them as `[redacted]`;
- per-case transcripts, tool arguments and retrieved sections;
- service endpoints, price source URLs and run start times.

**Checking it against the repository.** Check out the tree `ca76c9a` (commit `62eb7ed`) and confirm that the hashes of `eval/thresholds.json` and `eval/datasets/*.json` match those listed. The release gate run linked in the file holds the control's own reports as workflow artifacts for as long as GitHub keeps them.
