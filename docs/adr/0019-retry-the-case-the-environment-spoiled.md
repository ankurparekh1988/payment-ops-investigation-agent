# 0019. Retry the case the environment spoiled, not the whole run

- **Status:** Accepted
- **Date:** 2026-11-07

## Context

When the evaluation environment failed a case, the harness discarded the whole run and repeated it once; a second spoiled attempt ended the evaluation as "could not evaluate". That was reasonable when a failure meant the model or Search was down. It stopped fitting once every tool result went through Prompt Shields, Content Safety's harm analysis and a classifier ([ADR 0015](0015-screen-questions-and-tool-text-for-harm.md)): one value that couldn't be checked in time now spoils a case, and a run has 37 of them. An isolated failure late in a run threw away every result before it, and two unrelated failures in one run ended the evaluation.

Those endings also said little. [#54](https://github.com/ankurparekh1988/payment-ops-investigation-agent/pull/54)'s first evaluation ended with only `screening-unavailable`, which covers four checks and several ways of failing, and the workflow then spent about 41 more minutes on an evaluation that could no longer reach a verdict.

## Decision

**An attempt the environment spoiled is discarded and that case alone runs once more, in fresh services, after a minute. Only the retry's result and tickets count. A second spoiled attempt for the case, or a fourth case retry in one run, ends the evaluation as "could not evaluate".**

- **What triggers it:** only the failures the harness already called the environment's: the model or Search unavailable or busy, a sign-in failure, an endpoint that can't be reached, or tool text that couldn't be screened.
- **What never triggers it:** anything the system did. An incomplete answer, a refusal, a limit reached, an unverified assessment or a wrong tool is scored, and so is a question still unscreened on its second ask, whatever kept the screen from answering, a sign-in failure included ([ADR 0017](0017-ask-again-when-the-question-screen-cant-check.md)). The sign-in count shows when that's the cause.
- **The retry covers the whole case:** if the case's second ask is what the environment spoiled, the retry starts over from the first ask.
- **The cap:** three retries in a run of 37 is an isolated failure now and then. More than that is an outage, and results collected through one wouldn't measure the system. The cap was chosen before this rule had run, and it only ever turns a run into "could not evaluate", never into a pass.
- **Retrieval is unchanged:** one deterministic pass, repeated whole if spoiled.

**A retry repairs an attempt; it doesn't overturn a result.** This rule applies while results are being collected, and it can't see them. Whether an evaluation that finished and failed may run again is still [ADR 0016](0016-one-recorded-rerun-when-the-agent-is-unchanged.md)'s and [ADR 0018](0018-evaluation-thresholds-from-measured-run-to-run-spread.md)'s to decide. A quality failure never becomes an infrastructure retry.

**Thresholds, labels, held-out membership, the number of runs and the formula don't change.** Each case is still scored exactly once per run, so every denominator is what the labels give it. The thresholds stay valid because the results kept are the same kind as before: attempts the environment didn't spoil.

**Every report says what happened.** The harness counts, per check, requests, retries, throttling, server errors, timeouts and other failures; per screen, how many couldn't answer and whether they ran out of time; sign-in failures; and each case retry with its cause and the failures seen during that attempt. The job summary and the report carry them. When the evaluation can't be made, the summary and the annotation do, since no report is written then, and a fault in the harness itself leaves them in the summary too. A failure now reads "tool-text classifier: 2 throttled" rather than `screening-unavailable`.

- **Seen from outside.** Requests are read from the runtime's HTTP diagnostics, and the screens are wrapped where they're registered. The app isn't changed, so what's evaluated is what ships.
- **Counts and case ids only.** Never a question, tool text, prompt, endpoint, token or service answer. They stay on the report and job summary and don't go to Log Analytics.

**The workflow stops when continuing can't help.** After a gate failure (exit 1) the next evaluation still runs, because its result is still useful. After invalid input (exit 2), an unavailable environment (exit 3) or anything unexpected, it doesn't. The check's meanings don't change.

## Consequences

- One failed call costs one case and a minute, instead of a whole run or the evaluation.
- A screen failing often stays visible. Retries are counted in every report and capped per run, so a retry can't hide an outage, and the same counts show whether classifier traffic needs limiting, which is a change to AI behaviour and would be decided on that evidence in a pull request of its own.
- A discarded attempt is never looked at, so whatever it did can't fail a gate. That's the same as before, when a discarded run wasn't looked at either: an attempt the environment spoiled isn't evidence either way.
- At worst an evaluation waits nine more minutes: one minute for each of three retries in each of three runs.
- Three things are inferred rather than observed, because the app doesn't report them. A question screen ran out of time if it couldn't answer and took its full budget. A screen that couldn't answer with no failed request is "unexplained": a malformed answer, a refused sign-in or a missing endpoint. A Content Safety retry is recognised by the same request body being sent again, while the model SDK's retries show up only as more requests.
- Listening to the runtime's diagnostics makes it add a trace header to the evaluation's HTTP requests. It carries no content and the app's behaviour doesn't depend on it.

## Alternatives considered

- **Keep repeating whole runs.** Rejected: the cost grows with the number of screening calls, while the failures are isolated.
- **Retry a case until it succeeds, or without a cap.** Rejected: an outage would look like a slow pass.
- **Count failures inside the screens.** Rejected: it would change the agent under test, so it would be an AI behaviour change that can't share a pull request with gate policy, for numbers the harness can get from outside.
- **Limit classifier concurrency now.** Rejected for this change: it changes runtime behaviour, and nothing yet shows how much of the failure rate it would remove. These counts will.
