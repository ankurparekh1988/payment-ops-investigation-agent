# 0017. Ask a question again when the question screen couldn't check it

- **Status:** Accepted
- **Date:** 2026-11-02

## Context

Every question is screened before the investigation agent sees it ([ADR 0015](0015-screen-questions-and-tool-text-for-harm.md)). When a check can't answer within 8 s, the question ends *unscreened*: the agent never runs, and the person is told the question couldn't be checked and asked to try again. The screen's classifier shares `chat-blue` with the investigations, so under evaluation load Azure throttles it, and some questions end this way in every evaluation: 4 case runs in #43's, 1 in #44's and 1 in #44's rerun.

The evaluation scored an unscreened question against the agent. A case with no tool calls misses every required tool, every tool argument and all of its evidence, so one unscreened question can move a gating median by a whole case. In #44's rerun it decided the result: required tool recall missed its threshold by one tool, which the unscreened question had lost, while the same question was investigated normally in the other two runs.

The harness's rule for infrastructure failures doesn't fit. It discards the whole run and repeats it once, and a second spoiled attempt ends the evaluation as "could not evaluate". In the last three evaluations 5 of 9 runs had an unscreened question, so more than half of runs would be discarded, and evaluations would often end unevaluated.

## Decision

**A question that ends unscreened is asked once more, in fresh services, after 15 s. The second ask's result is the one scored, whatever it is. A second unscreened result counts against the system, as before.**

- **Why it's fair:** the agent never saw the first ask, so nothing it did is discarded, and asking again is what the app tells the person to do.
- **Why only once:** a screen that stays unavailable is a real failure people would meet, so it still costs the system.
- **The wait:** throttling is per minute, so asking again at once would mostly meet the same limit.
- **Visible in every report:** each case result records `reasked`, and the job summary says how many case runs were asked again and how many stayed unscreened, so a screen that fails often is still seen even when the second ask succeeds.
- **What's scored:** the second ask's outcome, tool calls, evidence, latency and tokens. The first ask made no investigation requests; its 8 s of screening isn't added to the latency.
- **Applies to both systems.** The fixed workflow doesn't screen questions yet, so for it nothing changes. (Amended 2026-11-05: the fixed-workflow challenger now screens its question with the same screen, so a question it couldn't check is asked again the same way.)

Thresholds, labels and the formula don't change. The rule changes how one outcome is collected, not what passes, and it applies to every evaluation from the change that adds it.

## Consequences

- The gate stops failing on an outcome the agent never had a chance to affect, while a screen that stays down still fails it.
- Evaluations of a change that only provisions the screen's own deployment, such as #44, are judged on the agent rather than on how busy `chat-blue` was.
- Unscreened questions remain a product problem until the classifier has its own deployment. The report's count is where that's tracked, and it should fall to near zero once the classifier moves.
- An evaluation now takes slightly longer: 15 s and one more screen for each re-asked question.

## Alternatives considered

- **Treat an unscreened question as an infrastructure failure.** Rejected: the whole run is discarded, and at the rate seen so far too many evaluations would end without a result.
- **Leave unscreened questions out of the denominators.** Rejected: which cases count comes from the labels, never from the outcome ([ADR 0012](0012-evaluation-thresholds-from-a-pre-registered-rule.md)).
- **Ask until it's screened.** Rejected: a screen that's down would never cost anything.
