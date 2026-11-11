# Architecture decision records

Significant design decisions, why they were made, and what they cost. Each record is short and written when the decision is made, next to the code it affects.

| ADR | Decision | Status |
|---|---|---|
| [0001](0001-terraform-for-infrastructure.md) | Terraform for infrastructure | Accepted |
| [0002](0002-pinned-model-versions.md) | Pinned model versions, upgraded through evaluation | Accepted |
| [0003](0003-server-rendered-ui-and-sign-in.md) | Server-rendered UI as its own backend, with credential-free sign-in | Accepted |
| [0004](0004-ai-release-manifest.md) | One AI release manifest, built into the application | Accepted |
| [0005](0005-indexing-by-pushing-from-the-pipeline.md) | Indexing by pushing from the pipeline | Accepted |
| [0006](0006-grounded-answers-with-a-server-owned-evidence-registry.md) | Grounded answers with a server-owned evidence registry | Superseded in part by [0010](0010-validated-assessments-with-per-claim-evidence.md) |
| [0007](0007-retrieval-strategy-by-frozen-calibration.md) | Choosing the retrieval strategy by measurement, with vectors deferred | Accepted |
| [0008](0008-data-ports-with-demo-adapters.md) | Data ports with demo adapters, proven by contract tests | Accepted |
| [0009](0009-investigation-agent-on-agent-framework.md) | One investigation agent on Agent Framework, with our own limits | Accepted |
| [0010](0010-validated-assessments-with-per-claim-evidence.md) | Validated assessments with per-claim evidence and server-observed gaps | Accepted |
| [0011](0011-the-model-proposes-the-server-executes.md) | The model proposes; the server executes after the owner approves | Accepted |
| [0012](0012-evaluation-thresholds-from-a-pre-registered-rule.md) | Evaluation thresholds from a pre-registered rule, applied once to a baseline of `main` | Accepted |
| [0013](0013-screen-untrusted-tool-text.md) | Screen untrusted tool text before the model reads it | Accepted |
| [0014](0014-safety-evaluation-from-a-pre-registered-rule.md) | Safety evaluation with its own hard gates and a threshold rule frozen before its baseline | Accepted; amended by 0020 |
| [0015](0015-screen-questions-and-tool-text-for-harm.md) | Screen questions before the investigation agent sees them, and tool text for harm | Accepted |
| [0016](0016-one-recorded-rerun-when-the-agent-is-unchanged.md) | One recorded rerun of a failed evaluation when the agent under test is unchanged | Accepted; amended by 0018 |
| [0017](0017-ask-again-when-the-question-screen-cant-check.md) | Ask a question again when the question screen couldn't check it | Accepted; amended by 0019 |
| [0018](0018-evaluation-thresholds-from-measured-run-to-run-spread.md) | Evaluation thresholds from measured run-to-run spread, and a branch update counts as the rerun | Accepted |
| [0019](0019-retry-the-case-the-environment-spoiled.md) | Retry the case the environment spoiled, not the whole run | Accepted |
| [0020](0020-evaluate-on-request-at-lower-cost.md) | Evaluate on request, once per change, at lower cost | Accepted |
