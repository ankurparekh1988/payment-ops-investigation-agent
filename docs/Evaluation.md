# Evaluation

How AI behaviour is measured, as built so far. Retrieval calibration is in place; the evaluation suite and release gate for answers and the agent come later.

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

Recorded here after the first run from `main`. Until then the app uses lexical retrieval with no floor.
