# 0020. Evaluate on request, once per change, at lower cost

- **Status:** Accepted
- **Date:** 2026-11-10

## Context

Every evaluation is paid for by the call: the agent's and the classifier's model tokens, and Content Safety by the text record. In the first ten days of November 2026 the subscription spent C$126, nearly all of it on those calls, against a monthly budget of C$10. The infrastructure itself is on free tiers or costs cents.

One evaluation, investigations and safety together, made about 6,600 Content Safety calls and 4,600 calls to the tool-text classifier, besides the agent's own requests. Several things multiplied that:

- **Every push to an evaluated pull request started a new evaluation,** including pushes made only to answer a review.
- **Behaviour and gate policy had to change in separate pull requests,** so a piece of work that needed both was evaluated twice.
- **Each case ran with an empty screening cache.** The app keeps screening verdicts for the life of its process, so a runbook section is screened once however many investigations read it. The harness gave every case fresh services, so it paid to screen the same sections and records in every case.
- **The safety evaluation ran three times** on every evaluated pull request, though its rates gate nothing ([ADR 0014](0014-safety-evaluation-from-a-pre-registered-rule.md)) and its hard gates fail on any one run.
- **`main` was evaluated weekly,** though it changes only through evaluated pull requests and the model version is pinned.

The money spent has to stay in proportion to what the system is worth. That's as true of an evaluation budget as of anything else.

## Decision

**A pull request is evaluated once it asks to be, as one piece of work, and the evaluation pays for no screening the app wouldn't.**

- **On request.** A pull request that needs evaluating is evaluated only while it carries the `evaluate` label. Until then the `AI evaluation` check fails with "add the evaluate label to run the AI evaluation", so nothing merges unevaluated. Adding the label starts the evaluation, and every push while it's there is evaluated again, so the head that merges is the head that was measured. A pull request that changes neither behaviour nor gate policy needs no label and passes as before.
- **Behaviour and gate policy may change in one pull request.** The work is evaluated once, under the workflow and code of the pull request itself. The bar still can't move silently. The classifier still runs from the base branch. A change to `eval/thresholds.json`, `eval/safety-thresholds.json` or a dataset still needs an ADR in the same pull request, saying what changed and why.
- **A run's cases share the screening cache,** as the app's investigations share it, and each run starts with an empty one, so runs stay independent measurements. A verdict the screen couldn't give is never cached, as in the app. The cache for harm verdicts is built inside the app's own registration, so it's shared only once the app registers it where the harness can reach it.
- **The safety evaluation runs once on a pull request,** and three times on scheduled and manual runs of `main`. Its hard gates still fail on any run.
- **`main` is evaluated monthly,** on the first of the month at 06:00 UTC, and on demand.

## Consequences

- An evaluated pull request costs one evaluation unless it's pushed to after it's labelled. Work that once needed a behaviour pull request and a gate-policy one needs one.
- Within a run, a section or record screened once isn't screened again. Each distinct planted text is still screened, because each one is new text.
- A planted instruction the agent follows only occasionally is less likely to be caught by one safety run than by three: the failure on [#58](https://github.com/ankurparekh1988/payment-ops-investigation-agent/pull/58) came in one run of three. Each evaluated pull request and each monthly run still samples it, and a follow in any run fails the check.
- A threshold change can now share a pull request with the behaviour it's judged against. The ADR that every threshold or dataset change needs is where that's argued, and the base branch's classifier is what enforces it.
- The investigation evaluation keeps three runs, because its thresholds gate the median of three ([ADR 0018](0018-evaluation-thresholds-from-measured-run-to-run-spread.md)).
- A month without activity on `main` goes unevaluated for up to a month instead of a week.

## Alternatives considered

- **Smaller datasets.** Fewer cases cost less, but the investigation thresholds were derived for these datasets' N, so a cut would need a new ten-run baseline, itself a large cost. Not now.
- **A cheaper model.** `gpt-5.4-nano` costs about a quarter of `gpt-5.4-mini` per token, but a model change needs a new baseline and may fail the investigation thresholds. The screen classifier, which makes most of the model calls, is the first candidate, measured on its own.
- **Dropping the harm check on tool text.** It's most of the screening cost, but the safety hard gates may depend on it. Decided only after the shared cache shows what it still costs.
- **Evaluating only before merge, with a manual workflow run.** The check on the pull request would no longer record what it measured. A label keeps the check, the result and the pull request together.
