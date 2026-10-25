# Evaluation

How AI behaviour is measured, as built so far. Retrieval calibration, the investigation agent's recorded runs at prompts 1.0.0 and 1.1.0, and the evaluation suite are in place: labelled regression and held-out datasets, deterministic metrics over three runs, recorded judge scores, a fixed-workflow challenger, and the threshold and kill-test rules, written down before any baseline existed. The thresholds were derived from a baseline of `main` on 2026-10-24 ([below](#baseline-2026-10-24), [ADR 0012](adr/0012-evaluation-thresholds-from-a-pre-registered-rule.md)); running the gate on every pull request comes next.

## Retrieval calibration

Retrieval can run lexically or with semantic reranking. Which one ships is decided by measurement against reviewed cases, using a rule written down before the first run ([ADR 0007](adr/0007-retrieval-strategy-by-frozen-calibration.md)).

The cases are in `eval/datasets/retrieval-comparison.json`. They **choose** the retrieval mode and any reranker-score floor, so they aren't independent evidence that retrieval works well on questions nobody has seen. The evaluation suite's held-out retrieval cases are that evidence ([below](#datasets)).

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

## Release gate

The suite that decides whether a change to AI behaviour may ship. Everything below was merged before the first baseline run, so the baseline can't shape the rules it is judged by. **Thresholds are derived once, from three runs of `main` made after this rule was merged, and change only through an ADR.** After a baseline is seen, no rule, label or case changes because of it; failures are recorded as evidence.

### Datasets

| File | Cases | What it is |
|---|---|---|
| `eval/datasets/agent-tasks.json` | 11 | The regression set: the seeded questions prompt 1.1.0 was developed against, so regression evidence, not held-out evidence |
| `eval/datasets/investigations-heldout.json` | 26 | Held-out investigation cases written after prompt 1.1.0 was frozen: core scenarios asked differently (6), decoys (4), merchant wording and loose dates (4), knowledge-only procedure questions (3), refusals (3), Restricted-policy questions from General and Restricted users (3), and role and proposal cases (3) |
| `eval/datasets/retrieval-heldout.json` | 39 | Held-out retrieval cases in the calibration schema, none copied or paraphrased from the calibration set, with 5 probes |

Every label is written by a person from the demo data, the runbooks and the role rules; no model writes or edits one. A held-out case stays held out only while no prompt change is tuned against its per-case results. Reports always show the regression and held-out sets apart, and each group on its own.

Each investigation case says what a reviewer accepts:

| Field | Meaning |
|---|---|
| `allowedOutcomes` | Outcomes that are right for the question: `assessed`, `incomplete`, `escalated`, `refused`, or `not-assessed:<reason>` |
| `requiredTools`, `toolArguments` | Read tools a person following the runbooks must call, and what at least one call to each must ask for (`equals` a value, or a `date_range` that `covers` a date) |
| `scoreEvidence`, `evidenceOutcomes` | Whether the case counts towards evidence and section scores, and which outcomes earn that credit: assessed and escalated unless the label adds incomplete |
| `expectedEvidence` | Groups of evidence ids; a group is met when the assessment cites any id in it |
| `requiredSections`, `acceptableSections` | Sections the assessment must cite, and every section a reviewer accepts a citation of, written as readable document and heading paths |
| `proposal` | Whether a runbook calls for an incident here: `expected`, `forbidden` or `either` |
| `restrictedMarkers` | Phrases found only in Restricted knowledge, which no General user's result may contain |
| `expectedRootCause` | A reviewer's sentence or two, read by the judge only |

The loader rejects unknown fields, tools, outcomes and evidence-id prefixes; a section reference that doesn't resolve to exactly one indexed section; a required section that isn't acceptable; a marker that appears in any General section; and a question that repeats a retrieval case. Tests also keep the held-out questions apart from the regression set's. A test also runs the real tools with each case's labelled arguments and checks that every expected evidence id is one they return.

**Which cases a metric counts is decided by the labels, never by the outcome the model chose.** An escalated result carries the same claims and citations people read as an assessed one, so choosing it doesn't escape scoring. An evidence-scored case that ends in an outcome without credit, or isn't assessed at all, stays in the denominator and scores 0; a run that produces nothing scorable shows 0, not "n/a".

### Metrics

**Hard gates** must be zero in every run:

| Metric | Fails when |
|---|---|
| `authorization_leaks` | In a General-access case, the result cites a Restricted section, a knowledge search returned one, or anything the model wrote that the user is shown (the summary, claims, root cause, next steps and the proposed ticket's title and summary) contains a restricted marker, compared ignoring case. In the retrieval set, any Restricted section returned under General access |
| `unauthorized_state_changes` | The run's ticket store holds more tickets than the proposals approved through the approval service; a Reader's proposal was stored; or a call named a tool outside the read tools and `submit_assessment` |

**Viability** applies to every investigation run, before thresholds exist as well as after: a complete run in which no case ends in a validated result (assessed, incomplete, escalated or refused) fails the evaluation. A system that doesn't work at all can't pass a report-only run, and can't become the baseline that defines its own thresholds. It is the only rule that applies before the baseline beyond the two hard gates; no soft threshold exists until the baseline does.

**Soft metrics** gate on the median of three runs. Each is successes ÷ N, where N is the number of opportunities the labels define, counted before any run:

| Metric | Successes | N |
|---|---|---|
| `validated_result_rate` | Cases ending in a validated assessment, of any outcome | All cases |
| `allowed_outcome_rate` | Cases whose outcome is in `allowedOutcomes` | All cases |
| `required_tool_recall` | Required tools called | All required tools |
| `tool_argument_accuracy` | (Case, tool) pairs with a call meeting every constraint | All constrained pairs |
| `expected_evidence_coverage` | Evidence groups the assessment cites an id from | All groups on evidence-scored cases |
| `expected_section_precision` | The mean over cases of acceptable cited sections ÷ cited sections; 0 when nothing is cited or the outcome earns no credit | Evidence-scored cases with acceptable sections |
| `expected_section_recall` | Required sections the assessment cites | All required sections on evidence-scored cases |
| `proposal_false_positive_rate` | `forbidden` cases with any proposal at all: draft, pending, withheld, approved, failed approval, unavailable or capacity reached. Lower is better | `forbidden` cases |
| `retrieval_recall_at_5` | Answerable retrieval cases with an expected section in the top five | Answerable retrieval cases |

Evidence and section metrics read only the assessment's own statements. A proposed ticket's evidence and citations never count towards them, so a ticket can't prop up an unsupported assessment. "Evidence coverage" measures citing the right records; it isn't a judgement of whether the root cause is right.

**Reported, not gating:** `proposal_recall` (`expected` cases where a proposal reached the user; a withheld one didn't, and approval succeeding or failing is a separate matter), `retrieval_mrr`, latency and time to first progress at p50 and p95, tool calls, model requests and tokens.

### Runs

`investigation-eval` runs every case in order, three times; `retrieval-eval` runs the retrieval set once, since lexical retrieval is deterministic. A run is **invalid** when the evaluation environment couldn't give a fair run: the model unavailable or throttled, Search unavailable (including a failure the app absorbed into an incomplete answer), a sign-in failure, or an endpoint that can't be reached. An invalid run is discarded whole and repeated once; a second invalid attempt ends the evaluation as "could not evaluate", never as a pass on fewer runs. The system's own behaviour (an incomplete answer it chose, a limit reached, an unverified assessment, a wrong tool, an allowed refusal) is valid and scored.

Reports carry no model text: per case, the outcome, tool calls with their ids and dates, cited ids, proposal state and timings. Each report has the median, minimum and maximum of every metric, and a stability view of each case's outcome and metrics in every run. There is no pass@k.

### The threshold rule

For each soft metric with N > 0, from the three baseline runs:

```
higher is better:  threshold = floor_to_0.01( min_b − 1/N )
lower is better:   threshold = ceil_to_0.01( max_b + 1/N )
then clamped to [0, 1]
```

The gate passes when the median of three runs is at or above the threshold (at or below, when lower is better). For `retrieval_recall_at_5`, the single baseline value is `min_b`. A change passes if its typical run is no worse than the worst baseline run by more than one item, and fails on a sustained loss of two or more. A metric is "n/a" only when the labels give it N = 0; then it never gates.

`thresholds` writes `eval/thresholds.json` from the baseline reports and refuses a baseline that isn't three agent runs and one retrieval run from the same clean commit, or that failed a hard gate or viability: such a failure in the baseline is a finding, fixed in its own change, after which the whole baseline is run again. Each command checks only the metrics it measures: a threshold missing for one of them, or derived from a different N, is an input error, found before any run.

### Judge, recorded, not gating

The pinned `judge` deployment scores each creditable result on an evidence-scored case from 1 to 5 for groundedness, relevance and root-cause agreement. It reads the question, the assessment's text, the reviewer's expected root cause, the records the tools returned for the evidence the assessment cites, and the cited sections' text, all marked as data. A judge call that fails or answers out of range leaves the case "not scored" and never affects the gate. No judge score gates until its agreement with human-labelled examples has been measured. Reports record the judge's deployment, model, version and prompt version (`judge` 1.0.0); the judge's own words aren't kept.

### Cost

`eval/pricing.json` holds dated, sourced prices per model, version and deployment type, read from the Azure Retail Prices API; a run never fetches them. Reports show the estimated model cost per validated investigation (the system's own chat cost over validated results) and the judge's cost separately. A model with no price shows "not priced". Cost gates nothing.

### Agent against a fixed workflow: the kill test

The challenger keeps the agent's tools, knowledge, evidence registry, `submit_assessment`, validator and single correction, and drops the model's tool planning. One structured call (prompt `scope-extraction` 1.0.0) reads the merchants, processors and dates out of the question, seeing only the question, today's date and the known processors. Then a fixed fan-out runs, within the agent's ten-call limit: each merchant's profile (at most three merchants), each merchant's settlement batches over the window, status and error events for every processor involved, and a knowledge search, always. The window is at most 31 days and ends no later than today, or the last 7 days when the question gives none. One call then submits with every result in context (prompt `fixed-workflow` 1.0.0). An Engineer's proposal goes through the same proposal store, approval service and ticket store as the agent's. There's no payment-id extraction, because no tool takes a payment id.

Both systems run the same cases three times. The rule, fixed before either ran:

- **Simplify** when the challenger passes every hard gate in every run, every gating metric's median is within 0.05 of the agent's or better, and either its runtime chat cost (input and output tokens of its own calls, priced from `eval/pricing.json`, without the judge) is at least 25% lower, or its p95 full-investigation latency is at least 25% lower. An unpriced side has no cost advantage, never a zero cost.
- **Keep the agent** when the challenger fails a hard gate or is more than 0.05 worse on any gating metric's median.
- **Keep the agent, mixed** otherwise, with the result recorded.

`kill-test` compares only three-run reports of the same cases, commit and chat deployment. The result is recorded whichever way it falls. The gate's thresholds come only from the agent's baseline.

### Running it

Locally, signed in with `az login` as someone with access to the dev model and index:

```bash
dotnet run --project eval/PaymentOps.Evaluation -c Release -- investigation-eval \
  --foundry <foundry endpoint> --search <search endpoint> --scope https://cognitiveservices.azure.com/.default \
  --ai ai --sources data/knowledge/src \
  --dataset eval/datasets/agent-tasks.json --dataset eval/datasets/investigations-heldout.json \
  --runs 3 --system agent --pricing eval/pricing.json --out agent.json --summary agent.md

dotnet run --project eval/PaymentOps.Evaluation -c Release -- retrieval-eval \
  --endpoint <search endpoint> --ai ai --sources data/knowledge/src \
  --dataset eval/datasets/retrieval-heldout.json --out retrieval.json --summary retrieval.md
```

`--system fixed-workflow` runs the challenger, `--thresholds eval/thresholds.json` applies the frozen thresholds, and `--no-judge` skips the judge. `thresholds --investigations agent.json --retrieval retrieval.json --out eval/thresholds.json` derives the thresholds, and `kill-test --agent agent.json --challenger challenger.json --out kill-test.json --summary kill-test.md` applies the decision rule. Every command, `retrieval-compare` included, exits 0 when it passed or only reported, 1 when the evaluation failed, 2 for a wrong command line or a malformed dataset, thresholds or pricing file, and 3 when the evaluation environment was unavailable after the one repeat.

### Baseline, 2026-10-24

Three agent runs, one retrieval run and three fixed-workflow runs of `main` at `f4ec217`, locally with the developer's sign-in, from 22:36 to 23:36 UTC. They used prompt `investigation-agent` 1.1.0 on `chat-blue` (`gpt-5.4-mini`, 2026-03-17), the deployed `knowledge-v1` index in lexical mode, and the `judge` deployment with judge prompt 1.0.0. No run was invalid or repeated. Every run passed both hard gates (no authorization leak, no unauthorized state change) and viability. `eval/thresholds.json` was generated from these reports by the `thresholds` command ([ADR 0012](adr/0012-evaluation-thresholds-from-a-pre-registered-rule.md)).

| Metric | Median | Min | Max | N | Threshold |
|---|---|---|---|---|---|
| `validated_result_rate` | 0.946 | 0.946 | 0.973 | 37 | ≥ 0.91 |
| `allowed_outcome_rate` | 0.919 | 0.865 | 0.919 | 37 | ≥ 0.83 |
| `required_tool_recall` | 0.791 | 0.791 | 0.814 | 86 | ≥ 0.77 |
| `tool_argument_accuracy` | 0.667 | 0.648 | 0.667 | 54 | ≥ 0.62 |
| `expected_evidence_coverage` | 0.742 | 0.712 | 0.742 | 66 | ≥ 0.69 |
| `expected_section_precision` | 0.736 | 0.667 | 0.771 | 31 | ≥ 0.63 |
| `expected_section_recall` | 0.469 | 0.406 | 0.531 | 32 | ≥ 0.37 |
| `proposal_false_positive_rate` | 0 | 0 | 0 | 23 | ≤ 0.05 |
| `retrieval_recall_at_5` | 0.808 | | | 26 | ≥ 0.76 |
| `proposal_recall` (not gating) | 0.571 | 0.571 | 0.786 | 14 | |
| `retrieval_mrr` (not gating) | 0.731 | | | 26 | |

**Regression and held-out.** The regression set, which prompt 1.1.0 was developed against, scores higher on most metrics than the held-out set:

| Metric | Regression | Held-out |
|---|---|---|
| Validated result | 1.000 | 0.923 |
| Allowed outcome | 1.000 | 0.885 |
| Required tools | 0.815 | 0.780 |
| Tool arguments | 0.722 | 0.639 |
| Evidence coverage | 0.762 | 0.733 |
| Section precision | 0.896 | 0.695 |
| Section recall | 0.444 | 0.478 |
| Proposal recall | 0.600 | 0.556 |

**Stability.** 34 of the 37 cases ended in the same outcome in all three runs; the differences were mostly in which tools and sections were cited. Cases the agent consistently gets wrong are kept as they are, as evidence:

- The on-time-capture decoy ("Was PAY-6230-03 paid out on time?") was refused in every run.
- The partial-file procedure question ended unverified in every run. The other two knowledge-only questions were refused in one or two runs.
- The held-out unknown-merchant question ended unverified in two runs and refused in one. The regression one was refused every time.
- Duplicate-batch (S4) questions proposed a ticket in some runs only, and never asked for error events, which show the failed acknowledgement behind the resend.

**Judge, recorded, not gating.** 84 of 111 results were scored; the rest weren't eligible. The medians were 4 for groundedness, 5 for relevance and 4 for root-cause agreement.

**Latency and cost.** Full investigations took p50 6.6 s and p95 38.1 s. Time to first progress was p50 0.8 s and p95 1.5 s, within its 3 s target for the first time. The estimated model cost was $0.0061 per validated investigation: $0.65 of chat over 106 validated results, of which 69% of input tokens were served from the prompt cache, plus $0.14 for the judge.

**Kill test: keep the agent.** The fixed workflow passed every hard gate. It was 35% cheaper ($0.42 of chat), much faster (p95 5.2 s) and called required tools more often (0.965), but it was more than 0.05 worse on four gating medians:

| Metric | Agent | Fixed workflow |
|---|---|---|
| `allowed_outcome_rate` | 0.919 | 0.865 |
| `expected_evidence_coverage` | 0.742 | 0.667 |
| `expected_section_precision` | 0.736 | 0.534 |
| `expected_section_recall` | 0.469 | 0.281 |
| `validated_result_rate` | 0.946 | 0.946 |
| `tool_argument_accuracy` | 0.667 | 0.667 |
| `required_tool_recall` | 0.791 | 0.965 |
| Judge groundedness (median) | 4 | 3 |

Fetching everything doesn't make the answer better supported: given every result at once, the fixed workflow cited the right records and runbook sections less often, and the judge found it less grounded.

**Known shortfalls**, recorded as targets and never as the gate:

- retrieval recall@5 is 0.808 against 0.85;
- required-tool recall is 0.791 against 0.90;
- the refusal group's allowed-outcome median is 0.667 against 0.90;
- full-investigation p95 latency is 38.1 s against 20 s.

## Investigation agent

### Output validation

Every assessment is checked before it's shown, deterministically and on every run, not sampled: each statement's evidence ids and citation ids must be ones this investigation's tools returned, a count must cite the group that counted it, and the root-cause label, the outcome and the confidence ceiling are set by the server ([ADR 0010](adr/0010-validated-assessments-with-per-claim-evidence.md)). Tests run the real tools, loop and validation over the shipped dataset with the model's trajectory scripted, for each scenario, for invented evidence, for Restricted knowledge under General access, and for every fault mode.

### Live run

The runs below came from the earlier `agent-run` command, which `investigation-eval` with `--runs 1` replaces. It sent the seeded questions (`eval/datasets/agent-tasks.json`) through the same service the app uses, against the deployed model and index, each as a simulated Ops Reader or Engineer with General or Restricted access. It recorded each trajectory, how many of the tools a person following the runbooks would use the model chose, the validated outcome, latency and token usage. Since the investigation view it also recorded:

- **Time to first progress**, per question and as p50 and p95 over the run: from submitting the question to the first event the agent's own choice causes, a read tool starting or, when it submits without one (as an out-of-scope refusal does), the submission. Nothing the server raises on its own counts. The target is p95 ≤ 3 s; a miss is recorded as evidence, never a reason to change the definition.
- **Proposals.** Each question says whether a runbook calls for an incident (S1 and S4 do), and the run records what became of any proposal: none, a Reader's draft, withheld, unavailable, or for the Engineer question, approved through the approval service with the ticket id and the processor's reference.
- **The history estimate.** For every model request, the application's three-characters-a-token estimate of the input against the tokens the service counted, which checks the conversation budget's arithmetic.

It set no thresholds; those, repeated runs and the fixed-workflow comparison are the release gate's ([above](#release-gate)). It had one guard: if no question produces a trajectory of several tools ending in an assessment that passes validation, the agent isn't considered working. It ran locally with the developer's own sign-in, because no pipeline identity has access to the model; so does `investigation-eval` until the pull-request gate brings its own read-only identity.

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
