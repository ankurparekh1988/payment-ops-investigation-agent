# 0007. Choosing the retrieval strategy by measurement, with vectors deferred

- **Status:** Accepted
- **Date:** 2026-10-16

## Context

Azure AI Search can rank lexically, rerank semantically, search vectors, or combine them. Each step adds cost and moving parts: vectors need embeddings at publish time, a new index schema and another identity with access to the embedding model. The knowledge corpus is small, about a hundred sections of reviewed runbooks and policies, where lexical ranking may well be enough. Picking a strategy because it's available, or tuning a threshold after seeing which numbers look good, would decide the architecture by taste.

## Decision

**Lexical retrieval is the starting default. Semantic reranking is implemented alongside it, and a rule written before any measurement decides between them.**

- The AI release chooses the mode (`retrieval.mode`) and an optional reranker-score floor (`retrieval.semanticFloor`); requests can't.
- `retrieval-compare.yml` runs reviewed calibration cases in both modes against the live index, as a manual run from `main`. It only queries, and it fails if a General user's question returns a Restricted section.
- The rule: a mode passes if every critical case finds an expected section in its top five. If only one mode passes, it's chosen. If both pass, semantic is chosen only if it improves mean reciprocal rank by at least 0.10. A floor is chosen only from unrelated questions, and only if it costs no answerable case its expected section.
- If neither mode passes, vector or hybrid search is designed before anything is built on top of retrieval.
- The workflow recommends; adopting a result is a reviewed change to the AI release. The outcome is recorded below, without rewriting this decision.

The rule and cases are described in [Evaluation](../Evaluation.md).

## Consequences

- No vectors, embeddings or index schema change until measurement shows they're needed. The index already supports a new generation if they are.
- Semantic reranking is enabled on the search service (its free plan) so it can be measured; the app uses it only if the rule chooses it.
- The calibration cases choose the strategy, so they can't also be used to show that it generalises. Held-out cases come with the evaluation suite.
- A rule fixed in advance can choose something a person would have overruled. That's the point: the reasons are written down before the results are known.

## Alternatives considered

- **Hybrid search from the start.** The common default, but it adds embeddings and a schema change before there's any evidence the corpus needs them.
- **Semantic by default.** Plausibly better, but unmeasured, and the free plan limits how many semantic queries a month are included.
- **Choosing a floor by eye after the run.** Easy to rationalise, and adjacent questions (on topic but unanswerable) would push it up until real evidence started disappearing. Refusing those is the model's job, not retrieval's.

## Outcome

Recorded after the first run from `main`.
