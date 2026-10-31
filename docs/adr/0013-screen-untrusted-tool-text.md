# 0013. Screen untrusted tool text before the model reads it

- **Status:** Accepted
- **Date:** 2026-10-29

## Context

The agent reads text written outside this system: merchant and processor names, processors' incident summaries and references, closure-day reasons, error codes, and the titles, paths and content of knowledge sections. Any of it can carry an instruction aimed at the model, and an instruction that steers an investigation can steer a ticket proposal too.

The deployment's content filter doesn't see this text. For chat completions Azure screens only the latest user content, and document-attack detection covers only text tagged `<documents>` in a user or system message; tool messages are screened only by the Foundry Agent Service, which this agent doesn't use. A question the filter refused also ended as "the service isn't responding", and every earlier turn, including a refused attack, went back to the model with each later question.

## Decision

**Every externally authored string is marked where a tool writes it and screened in one place before the model reads it. What can't be shown clean is withheld, not failed.**

- **Marking by provenance.** Tools mark every externally authored value, whatever it looks like: an error code such as `SYSTEM:OVERRIDE_RULES` looks like a code too. Only server-owned values stay unmarked: ids, dates, amounts, enum words and values checked against a fixed set. A test poisons every string in the demo data and the knowledge, runs every tool through a screen that flags the poison, and fails if any poison reaches the model.
- **Free text stays marked, clean labels read plain.** Incident summaries, closure reasons and knowledge are wrapped in `<untrusted-data>` and keep the markers when clean. Names, codes and references are marked as labels: screened the same way, withheld the same way, but once clean the model reads the plain value. With every name wrapped, the model rarely widened a settlement search to the day before, and tool argument accuracy fell below the gate. On the three "yesterday" cases, ten runs each against the dev deployment, tool arguments were right 11 times in 90 with names wrapped against 43 on the code before this change; run back to back later, the code before this change scored 31 and labels 33. Scores drift between sessions, so only runs made together are compared.
- **One chokepoint.** The tool guard screens every marked value of a tool result with Prompt Shields before the result goes to the model.
- **Withhold, don't fail.** A value flagged as an attack is replaced by "[withheld: this text was flagged as a possible instruction to the assistant]". One that couldn't be checked (a throttle, a server error, a timeout, an authentication failure or a malformed answer) is replaced by "[withheld: this text couldn't be checked]". The investigation continues on the structured data, and the result shows a gap.
- **Withheld knowledge supports nothing.** A section with any part withheld keeps its citation id, so a person can still find and open it, but it can't support a statement, the knowledge half of an *Assessment* root cause, high confidence or a ticket. A section later read in full becomes support again.
- **One budget.** A guarded call has 8 s from starting the tool to the end of screening. The tool keeps its 5 s timeout, and screening gets at most 3 s of what remains; screening that runs out withholds.
- **Long text in overlapping pieces.** A call takes at most five documents and 10,000 characters, so text is cut into pieces of at most 9,500 characters overlapping by 500, and an instruction across a boundary lands whole in one piece. Any attack piece flags the value; otherwise any unchecked piece makes it unscreenable.
- **A cache.** Clean and attack verdicts are cached by the text's SHA-256, up to 2,000 entries, least recently used first out. "Couldn't be checked" is never cached.
- **A role with one action.** `<prefix> Prompt Shields User` holds only `Microsoft.CognitiveServices/accounts/ContentSafety/text:shieldprompt/action`. It's created by the bootstrap and assigned on the Foundry account only, to the app, the evaluation identity and developers.
- **Blocked is an outcome.** A prompt the content filter rejects, or a completion it withholds, ends the investigation as *blocked* with a fixed notice, without a retry.
- **History is an allowlist.** Only turns assessed as `assessed`, `incomplete` or `escalated` are sent with later questions. Blocked, refused and unassessed questions stay on the page and are never sent to the model again.
- **REST, not the SDK.** The stable `Azure.AI.ContentSafety` 1.0.0 has no Prompt Shields operation; only `1.1.0-beta.1` adds it, against a preview API version. The screen calls `text:shieldPrompt` with API version `2024-09-01` and an Entra token.

## Consequences

- Untrusted text reaches the model only after Prompt Shields passed it, and a Prompt Shields outage degrades an investigation rather than stopping it.
- A tool result with untrusted text costs one more call, a few hundred milliseconds when it isn't cached, and Prompt Shields is billed per text record. Names and codes repeat, so the cache absorbs most of them.
- A false positive withholds benign text. The corpus and the demo data as the model sees them, 304 strings, were screened before this change with none flagged; the safety evaluation measures it from now on.
- A person viewing a source page sees the original text: the screen protects the model's context, not the source an authorized person reads.
- Prompt Shields is listed for Canada Central under Foundry guardrails but not under the standalone Content Safety service. A successful call on our Canada Central account doesn't prove where the operation is processed, so this stays an open question ([Security](../Security.md#injection-and-content-safety)).

## Alternatives considered

- **Foundry's document-attack filter.** It screens only text tagged `<documents>` in a user or system message, and blocks the whole request when it trips. Tool results would have to move into the user message, and one bad incident note would end the investigation instead of losing one value.
- **Leaving names unscreened.** It recovers the same accuracy, but names come from onboarding and processors' own records, outside this system, and a name can carry an instruction as well as a note can.
- **Spotlighting** (encoding or interleaving untrusted text). It costs tokens on every call, reduces rather than detects, and the model starts talking about the encoding.
- **A second Prompt Shields call on the question.** The deployment's filter already screens the latest user message for prompt attacks on every call, and with the history allowlist a refused question is never resent.
- **Cognitive Services User.** It includes the shield action, along with every other data action on the account.
