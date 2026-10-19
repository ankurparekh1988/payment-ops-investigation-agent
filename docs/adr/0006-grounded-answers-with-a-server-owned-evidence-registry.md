# 0006. Grounded answers with a server-owned evidence registry

- **Status:** Accepted
- **Date:** 2026-10-16

## Context

An answer is only useful if every fact in it can be traced to a section the user was allowed to see. The model can't be trusted to keep to that: it can cite something it wasn't given, invent a link, answer from general knowledge, or produce text that looks like a citation but isn't one. Some users mustn't see Restricted sections at all, so a citation is also an access question.

Structured Outputs with a strict JSON schema fix the shape of a response, but the schema language Azure accepts can't say that a string is non-empty, that an array has at least one item, or that its items are unique.

## Decision

**The server decides what can be cited and checks every answer before it's shown.**

- Retrieval returns at most five sections the user may see. Each gets an id for this question only, `S1` to `S5`, held by the server with the section's key, title and path.
- The model receives the question and the sections in delimiters, and must return a strict JSON object: `status` (`answered` or `refused`), `answer_markdown`, `citation_ids` and `refusal_reason`. Reasoning effort is set to none: this is synthesis over a few supplied sections, not planning.
- The server rejects anything other than a normal completion, then checks the status rules the schema can't express, then the citations: the `[Sn]` markers in the answer and the declared ids must be exactly the same set, and every id must be one it issued for this question. Only exact markers in ordinary text count, using one definition shared with the renderer.
- Any failure turns the whole answer into a fixed message. A bad citation is never dropped so that the rest can be shown, and generation isn't retried.
- Citations shown to the user are built from the server's records. The model never supplies a URL, key or title.
- With no sections the user may see, the model isn't called.

## Consequences

- A citation always points at a section the user could see when the question was asked, and the source page checks access again when it's opened.
- Some answers that would have been right are refused because of a formatting slip by the model. That's the intended trade: an unverifiable answer is worse than none. How often it happens will be measured by the evaluation suite.
- The ids are meaningless outside one question, so nothing persists them.
- The prompt asks the model to treat section text as evidence, not instructions. That's guidance; the controls are the filter, the citation checks and the renderer.

## Alternatives considered

- **Plain text with inline markers.** Simpler to prompt, but a refusal has to be recognised from wording, and there's no declared list to check the markers against.
- **A citation list per claim.** The strongest grounding signal, but more complex to prompt and render than one question and one answer need. It fits the agent's structured assessment better and will be considered there.
- **Dropping unknown citations and showing the rest.** Friendlier, but it shows text whose support the server has just failed to confirm.
