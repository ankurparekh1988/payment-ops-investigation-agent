# Evaluation

How AI behaviour is measured, as built so far. Retrieval calibration and the investigation agent's live-run harness are in place, with one pre-merge diagnostic run; the recorded run from `main`, the evaluation suite and the release gate for answers and the agent come later.

## Retrieval calibration

Retrieval can run lexically or with semantic reranking. Which one ships is decided by measurement against reviewed cases, using a rule written down before the first run ([ADR 0007](adr/0007-retrieval-strategy-by-frozen-calibration.md)).

The cases are in `eval/datasets/retrieval-comparison.json`. They **choose** the retrieval mode and any reranker-score floor, so they aren't independent evidence that retrieval works well on questions nobody has seen; held-out cases come with the evaluation suite.

| Kind | Cases | Used for |
|---|---|---|
| `critical` | 15: at least one per document, two of them asked with Restricted access | The bar each mode must meet |
| `paraphrase` | 5: the same facts in different words | Mean reciprocal rank; where semantic ranking should help |
| `adjacent` | 5: on topic, but not answered by any document | Recorded only. Search finding a related section is right; refusing is the model's job |
| `unrelated` | 5: off topic | Choosing a reranker-score floor |
| `probe` | 3: Restricted-only questions asked with General access | A security check: any Restricted section returned fails the run |

Expected sections are written as a document id and heading path, such as *Risk hold review policy > Risk rules > RF-51 expired verification*, so a reviewer can read them. CI resolves every one to exactly one indexed section, so renaming a heading breaks the build rather than the measurement.

### How it runs

`.github/workflows/retrieval-compare.yml`, started by hand from `main` ([Deployment](Deployment.md#continuous-deployment)). Each case runs in both modes with no floor, as a synthetic user with the case's access, through the same code the app uses. It only queries the index. The results go to the job summary and a JSON artifact, with observations (each case's top five, the expected section's rank, reranker scores) kept apart from the rule's result.

### The rule

1. A Restricted section returned for a General question, in either mode, fails the run.
2. A mode passes when every `critical` case has an expected section in its top five.
3. Neither passes: retrieval needs more than lexical and semantic ranking, so vector or hybrid search is designed before building on it.
4. Only one passes: that one.
5. Both pass: semantic only if its mean reciprocal rank is at least 0.10 higher; otherwise lexical, which is simpler and cheaper.
6. If semantic is chosen, the floor is the highest of 0.25, 0.50 … 4.00 at which every `critical` and `paraphrase` case keeps at least one expected section, and at least half the `unrelated` cases have no hits left. If none does, there's no floor; an answerable case that semantic ranking already missed therefore means no floor.

Mean reciprocal rank is the average, over `critical` and `paraphrase` cases, of 1 / the rank of the first expected section in the top five (0 if it isn't there).

The workflow recommends; it never changes `ai/manifest.yaml`. Adopting a result is a reviewed change to `retrieval.mode` and `retrieval.semanticFloor`.

### Results

First run from `main`, 2026-10-20 ([run](https://github.com/ankurparekh1988/payment-ops-investigation-agent/actions/runs/39411260027)): **lexical, no floor.** No Restricted section came back for any probe, in either mode.

| | Lexical | Semantic |
|---|---|---|
| `critical` cases with an expected section in the top five | 15 of 15 | 15 of 15 |
| `critical` cases ranked first / second | 13 / 2 | 13 / 2 |
| `paraphrase` cases with an expected section in the top five | 1 of 5 | 4 of 5 |
| Mean reciprocal rank | 0.75 | 0.8225 |

Both modes met the critical bar, and semantic ranking's mean reciprocal rank was 0.0725 higher, short of the 0.10 the rule requires, so lexical stays. No floor would have qualified either: semantic ranking found no expected section for one paraphrase case, so no floor could keep one for every answerable case.

The difference is in the paraphrases, where lexical ranking found an expected section for one question in five and semantic ranking for four. These are the cases that chose the strategy, so this isn't held-out evidence of retrieval quality. Paraphrased and differently worded questions will be a held-out slice of their own in the evaluation suite, and a large semantic advantage there would be new evidence for a new decision.

## Investigation agent

### Output validation

Every assessment is checked before it's shown, deterministically and on every run, not sampled: each statement's evidence ids and citation ids must be ones this investigation's tools returned, a count must cite the group that counted it, and the root-cause label, the outcome and the confidence ceiling are set by the server ([ADR 0010](adr/0010-validated-assessments-with-per-claim-evidence.md)). Tests run the real tools, loop and validation over the shipped dataset with the model's trajectory scripted, for each scenario, for invented evidence, for Restricted knowledge under General access, and for every fault mode.

### Live run

`PaymentOps.Evaluation agent-run` sends about ten seeded questions (`eval/datasets/agent-tasks.json`) through the same service the app uses, against the deployed model and index, each as a simulated Ops Reader with General or Restricted access. It records each trajectory, how many of the tools a person following the runbooks would use the model chose, the validated outcome, latency and token usage.

It sets no thresholds; those, repeated runs and a fixed-sequence baseline belong to the evaluation suite. It has one guard: if no question produces a trajectory of several tools ending in an assessment that passes validation, the agent isn't considered working. It runs locally with the developer's own sign-in, because no pipeline identity has access to the model.

**Pre-merge diagnostic, 2026-10-21.** Run locally from the feature branch against the deployed model and index, before the change merged. It's a check that the agent works, not the recorded result. Nine of ten questions ended in a validated result, eight assessed and one refused, so the guard passed; the model chose two to six tools each time. The question about a merchant that doesn't exist ended unverified, because a lookup that finds nothing registers no evidence to cite. Latencies were 5.6 to 21.9 s. The run came before two rule changes: confidence judged per statement, and refusals shown as the server's fixed notice.

**Recorded run.** Still to come: the run from `main` after merge, recorded here with its date and results.
