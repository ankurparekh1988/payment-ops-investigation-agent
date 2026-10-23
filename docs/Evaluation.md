# Evaluation

How AI behaviour is measured, as built so far. Retrieval calibration and the investigation agent's live run are in place, with pre-merge diagnostics and recorded runs from `main` at prompts 1.0.0 and 1.1.0. The evaluation suite and the release gate for the agent come later.

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

`PaymentOps.Evaluation agent-run` sends about ten seeded questions (`eval/datasets/agent-tasks.json`) through the same service the app uses, against the deployed model and index, each as a simulated Ops Reader or Engineer with General or Restricted access. It records each trajectory, how many of the tools a person following the runbooks would use the model chose, the validated outcome, latency and token usage. Since the investigation view it also records:

- **Time to first progress**, per question and as p50 and p95 over the run: from submitting the question to the first event the agent's own choice causes, a read tool starting or, when it submits without one (as an out-of-scope refusal does), the submission. Nothing the server raises on its own counts. The target is p95 ≤ 3 s; a miss is recorded as evidence, never a reason to change the definition.
- **Proposals.** Each question says whether a runbook calls for an incident (S1 and S4 do), and the run records what became of any proposal: none, a Reader's draft, withheld, unavailable, or for the Engineer question, approved through the approval service with the ticket id and the processor's reference.
- **The history estimate.** For every model request, the application's three-characters-a-token estimate of the input against the tokens the service counted, which checks the conversation budget's arithmetic.

It sets no thresholds; those, repeated runs and a fixed-sequence baseline belong to the evaluation suite. It has one guard: if no question produces a trajectory of several tools ending in an assessment that passes validation, the agent isn't considered working. It runs locally with the developer's own sign-in, because no pipeline identity has access to the model.

**Pre-merge diagnostic, 2026-10-21.** Run locally from the feature branch against the deployed model and index, before the change merged. It's a check that the agent works, not the recorded result. Nine of ten questions ended in a validated result, eight assessed and one refused, so the guard passed; the model chose two to six tools each time. The question about a merchant that doesn't exist ended unverified, because a lookup that finds nothing registers no evidence to cite. Latencies were 5.6 to 21.9 s. The run came before two rule changes: confidence judged per statement, and refusals shown as the server's fixed notice.

**Recorded run, 2026-10-21.** Run locally from `main` at 8d2e420, with `investigation-agent` prompt 1.0.0 on the `chat-blue` deployment (`gpt-5.4-mini`, 2026-03-17), reasoning effort none, against the deployed index. One run per question, so it shows behaviour, not rates.

| Question | Access | Tools before submitting | Runbook tools chosen | Outcome | Latency |
|---|---|---|---|---|---|
| s1-late-payout | General | merchant, settlement, processor status ×2, knowledge | 4 of 5 | assessed, medium | 23.0 s |
| s1-missing-monday | General | merchant, settlement, processor status, knowledge | 3 of 4 | assessed, medium | 17.2 s |
| s2-closure-day | General | merchant, settlement, processor status, knowledge | 2 of 2 | assessed, high | 14.7 s |
| s2-later-than-usual | General | merchant, settlement, processor status, knowledge | 2 of 2 | assessed, high | 13.3 s |
| s3-hold-general | General | merchant ×2, knowledge | 2 of 2 | assessed, high | 9.2 s |
| s3-hold-restricted | Restricted | merchant ×2, knowledge | 2 of 2 | assessed, medium | 10.1 s |
| s4-paid-twice | General | merchant, settlement, processor status, knowledge | 2 of 3 | assessed, medium | 13.2 s |
| s4-batch-sent-twice | General | merchant, settlement, knowledge | 2 of 3 | assessed, medium | 10.7 s |
| unknown-merchant | General | merchant | 1 of 1 | refused | 3.3 s |
| action-request | General | merchant, knowledge | none expected | refused | 8.7 s |

All ten ended in a validated result, so the guard passed. Every root cause given was labelled an assessment, with no observed gaps. The trajectories match the pre-merge diagnostic closely; differences are recorded, not acted on:

- The model never asked for error events in the S4 questions or in s1-missing-monday, and this time skipped them in s1-late-payout too. It also skipped processor status in s4-batch-sent-twice.
- In both S3 questions it fetched the merchant profile twice with the same arguments; the repeat limit allows that.
- The question about a merchant that doesn't exist now ends refused, so the person sees the out-of-scope notice rather than "no such merchant". Refusals no longer need evidence, and the model chose that outcome.
- Confidence fell from high to medium in three assessed questions, as expected once each statement has to cite both data and knowledge for high. Refusals now always show low.
- The slowest observed question took 23.0 s, exceeding the 20 s full-investigation target value. This single run doesn't estimate p95. The action request needed one correction of its submission.

**Pre-merge diagnostic for the investigation view, 2026-10-22.** Run locally from the feature branch with `investigation-agent` prompt 1.1.0, before the change merged, on the same deployment and index. A check that the agent and the approval path work, not the recorded result; one run per question.

- Ten of eleven questions ended in a validated result, nine assessed and the action request refused, so the guard passed. The question about a merchant that doesn't exist ended unverified this time, after one correction; in the recorded run it was refused.
- All three S1 questions proposed a ticket. The two Reader questions showed it as a draft; the Engineer question's proposal was approved through the approval service and created `INC-0001`, carrying Fabrikam's own reference `FAB-7731` from the incident data. Neither S4 question proposed one, although the duplicate-batch runbook says to raise an incident. No other question proposed one.
- Time to first progress was p50 2.0 s and p95 5.4 s over the eleven questions, short of the 3 s target. The 5.4 s was the run's first question; the rest were 1.7 to 2.5 s. Full latencies were 6.5 to 23.7 s, with two S1 questions over 20 s.
- The three-characters-a-token estimate overstated the input the service counted by 1.4 to 1.9 times across 51 model requests, so the history budget errs on the safe side and no tokenizer is added.

**Recorded run, 2026-10-23.** Run locally from `main` at 4add5e4, with `investigation-agent` prompt 1.1.0 on the same `chat-blue` deployment (`gpt-5.4-mini`, 2026-03-17), reasoning effort none, against the deployed index. One run per question, so it shows behaviour, not rates.

| Question | Access, role | Tools before submitting | Runbook tools chosen | Outcome | Proposal (runbook calls for one) | First progress | Latency |
|---|---|---|---|---|---|---|---|
| s1-late-payout | General, Reader | merchant, settlement, processor status ×2, error events, knowledge | 5 of 5 | assessed, medium | none (yes) | 4.3 s | 20.6 s |
| s1-late-payout-engineer | General, Engineer | merchant, settlement, processor status ×2, knowledge | 4 of 5 | assessed, medium | approved: `INC-0001`, `FAB-7731` (yes) | 1.6 s | 13.0 s |
| s1-missing-monday | General, Reader | merchant, settlement, processor status, error events, knowledge | 4 of 4 | assessed, medium | draft (yes) | 1.5 s | 12.4 s |
| s2-closure-day | General, Reader | merchant, settlement, processor status, knowledge | 2 of 2 | assessed, high | none (no) | 1.6 s | 11.1 s |
| s2-later-than-usual | General, Reader | merchant, settlement, processor status, knowledge | 2 of 2 | assessed, high | none (no) | 1.5 s | 11.3 s |
| s3-hold-general | General, Reader | merchant, knowledge | 2 of 2 | assessed, high | none (no) | 1.2 s | 6.1 s |
| s3-hold-restricted | Restricted, Reader | merchant, knowledge | 2 of 2 | assessed, high | none (no) | 1.3 s | 6.2 s |
| s4-paid-twice | General, Reader | merchant, settlement, knowledge | 2 of 3 | assessed, medium | draft (yes) | 1.4 s | 10.2 s |
| s4-batch-sent-twice | General, Reader | merchant, settlement, knowledge | 2 of 3 | assessed, medium | none (yes) | 1.6 s | 9.8 s |
| unknown-merchant | General, Reader | merchant | 1 of 1 | refused | none (no) | 1.6 s | 5.2 s |
| action-request | General, Reader | merchant, knowledge | none expected | refused | none (no) | 1.3 s | 5.3 s |

All eleven ended in a validated result, nine assessed and two refused, so the guard passed. Every root cause given was labelled an assessment, with no observed gaps. Differences from the pre-merge diagnostic and the 1.0.0 run are recorded, not acted on:

- Proposals came in three of the five questions whose runbook calls for an incident. The Engineer's S1 proposal was approved through the approval service and created `INC-0001` with Fabrikam's reference `FAB-7731`; s1-missing-monday and s4-paid-twice showed drafts. The Reader's s1-late-payout proposed none, where the diagnostic proposed one, and s4-batch-sent-twice again proposed none. This is the first S4 proposal observed. No question whose runbook doesn't call for one proposed a ticket.
- The question about a merchant that doesn't exist ended refused after one correction, as in the 1.0.0 run; in the diagnostic it ended unverified.
- The model asked for error events in two S1 questions this time, but not for the Engineer's S1 question or either S4 question, and it skipped processor status in both S4 questions. Neither S3 question repeated the merchant lookup.
- Time to first progress was p50 1.5 s and p95 4.3 s over the eleven questions, short of the 3 s target. As in the diagnostic, the slowest was the run's first question; the rest were 1.2 to 1.6 s.
- Latencies were 5.2 to 20.6 s. The slowest observed question exceeded the 20 s full-investigation target value; this single run doesn't estimate p95.
- The three-characters-a-token estimate overstated the input the service counted by 1.46 to 1.91 times across 49 model requests. The largest request counted 3,678 input tokens, against the 32,000-token budget.
